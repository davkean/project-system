// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.VisualStudio.ProjectSystem.Utilities;
using Microsoft.VisualStudio.Threading;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.ProjectSystem
{
    /// <summary>
    /// A service that bridges CPS Core snapshots to the VS UI thread.
    /// </summary>
    /// <typeparam name="TInput">The snapshot that gets processed for later publishing to the UI thread.</typeparam>
    /// <typeparam name="TOutput">The data that is used to apply changes to the UI thread.  A superset of <typeparamref name="TApplied"/>.</typeparam>
    /// <typeparam name="TApplied">The snapshot that gets published to the UI thread.  This type should be immutable.</typeparam>
    internal abstract class ProjectHostBridge<TInput, TOutput, TApplied> : OnceInitializedOnceDisposedAsync
        where TOutput : class
        where TApplied : class
    {
        /// <summary>
        /// A bag of values to dispose when this instance is disposed of.
        /// </summary>
        private readonly DisposableBag disposableBag = new DisposableBag(CancellationToken.None);

        /// <summary>
        /// The link that brings data from external sources into this service.
        /// </summary>
        private IDisposable firstLink;

        /// <summary>
        /// The first block within this service, that should be Completed when this instance is disposed.
        /// </summary>
        private IDataflowBlock firstBlock;

        /// <summary>
        /// The block which broadcasts the applied value.
        /// </summary>
        private IReceivableSourceBlock<TApplied> appliedValueBlock;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectHostBridge{TInput, TOutput, TApplied}"/> class.
        /// </summary>
        protected ProjectHostBridge(JoinableTaskContextNode joinableTaskContextNode)
            : base(joinableTaskContextNode)
        {
        }

        /// <summary>
        /// Gets the direct access service.
        /// </summary>
        [Import]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by MEF")]
        protected IProjectLockService ProjectLockService { get; private set; }

        /// <summary>
        /// Gets the thread handler for the current threading model.
        /// </summary>
        [Import]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by MEF")]
        protected IProjectThreadingService ThreadingService { get; private set; }

        /// <summary>
        /// Gets the project fault handler service.
        /// </summary>
        [Import]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by MEF")]
        protected IProjectFaultHandlerService ProjectFaultHandlerService { get; private set; }

        /// <summary>
        /// Gets the unconfigured project.
        /// </summary>
        [Import]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by MEF")]
        protected UnconfiguredProject UnconfiguredProject { get; private set; }

        /// <summary>
        /// Gets the CPS async task services.
        /// </summary>
        protected abstract IProjectAsynchronousTasksService ProjectAsynchronousTasksService { get; }

        /// <summary>
        /// Gets the value that was most recently applied to the UI thread.
        /// </summary>
        protected internal TApplied AppliedValue { get; protected set; }

        /// <summary>
        /// Gets a block that broadcasts the applied value.
        /// </summary>
        protected internal IReceivableSourceBlock<TApplied> AppliedValueBlock
        {
            get { return appliedValueBlock; }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="Initialize()"/> method should block
        /// until the <see cref="AppliedValue"/> property has its first value.
        /// </summary>
        protected virtual bool BlockInitializeOnFirstAppliedValue
        {
            get { return true; }
        }

        /// <summary>
        /// Initializes this instance synchronously.
        /// </summary>
        protected void Initialize()
        {
            JoinableFactory.Run(() => InitializeAsync());
        }

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        protected override sealed async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            var firstValuePublishedSource = new TaskCompletionSource<object>();
            var debugTypeName = GetType().Name;

            await ProjectAsynchronousTasksService.LoadedProjectAsync(
                async delegate
                {
                    await InitializeInnerCoreAsync(cancellationToken);
                });

            TOutput lastOutputToBeApplied = default(TOutput);
            var preprocessingBlock = DataflowExtensions.CreateSelfFilteringTransformBlock<TInput, TOutput>(
                async input =>
                {
                    Report.If(ProjectLockService.IsAnyPassiveLockHeld, "We should not be in a lock");

                    var newOutput = await this.PreprocessAsync(input, lastOutputToBeApplied);
                    bool apply = this.ShouldValueBeApplied(lastOutputToBeApplied, newOutput);
                    if (apply)
                    {
                        lastOutputToBeApplied = newOutput;
                    }

                    return new KeyValuePair<TOutput, bool>(newOutput, apply);
                },
                new ExecutionDataflowBlockOptions
                {
                    CancellationToken = ProjectAsynchronousTasksService.UnloadCancellationToken,
                    NameFormat = string.Concat(debugTypeName, " Input: {1}")
                });
            var applicationBlock = new TransformBlock<TOutput, TApplied>(
                async output =>
                {
                    return await JoinableFactory.RunAsync(async delegate
                    {
                        await JoinableFactory.SwitchToMainThreadAsync(ProjectAsynchronousTasksService.UnloadCancellationToken);
                        await ApplyAsync(output);
                        firstValuePublishedSource.TrySetResult(null);
                        return AppliedValue;
                    });
                },
                new ExecutionDataflowBlockOptions
                {
                    // We *don't* use this.JoinableFactory.MainThreadScheduler because it
                    // doesn't have insight into asynchronous work (only one synchronous piece of it)
                    // which can cause deadlocks for async work since there is no joinable task
                    // that spans the entire async delegate that is passed to the dataflow block.
                    CancellationToken = ProjectAsynchronousTasksService.UnloadCancellationToken,
                });
            var appliedBlock = new BroadcastBlock<TApplied>(null, new DataflowBlockOptions() { NameFormat = string.Concat(debugTypeName, ": {1}") });

            preprocessingBlock.LinkTo(applicationBlock, new DataflowLinkOptions { PropagateCompletion = true });
            applicationBlock.LinkTo(appliedBlock, new DataflowLinkOptions { PropagateCompletion = true });

            firstBlock = preprocessingBlock;
            appliedValueBlock = appliedBlock.SafePublicize();
            firstLink = this.LinkExternalInput(preprocessingBlock);
            ProjectFaultHandlerService.RegisterFaultHandler(appliedValueBlock.Completion, severity: ProjectFaultSeverity.LimitedFunctionality, project: UnconfiguredProject);

            // If the derived type's InitializeCoreAsync method sets the initial value,
            // that is our indication that we don't need to block here.
            if (BlockInitializeOnFirstAppliedValue && AppliedValue == null)
            {
                // Await in such a way that if the Dataflow blocks have faulted, we throw instead of hanging indefinitely.
                var completingTask = await Task.WhenAny(firstValuePublishedSource.Task, preprocessingBlock.Completion, applicationBlock.Completion);
                await completingTask; // rethrow if applicable
            }
        }

        /// <summary>
        /// Initializes state in the derived types.
        /// </summary>
        /// <returns>A task whose completion signals that initialization is finished.</returns>
        /// <remarks>
        /// This method might be invoked on the background thread.
        /// </remarks>
        protected abstract Task InitializeInnerCoreAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Links this service's external data source(s) to the specified block.
        /// </summary>
        /// <param name="targetBlock">The block to link to.</param>
        /// <returns>A value whose disposal will terminate the link.</returns>
        /// <remarks>When appropriate for the derived type, completion propagation may be set in the link.</remarks>
        protected abstract IDisposable LinkExternalInput(ITargetBlock<TInput> targetBlock);

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        protected override Task DisposeCoreAsync(bool initialized)
        {
            disposableBag.Dispose();
            firstLink?.Dispose();
            if (firstBlock != null)
            {
                firstBlock.Complete();
            }

            return TplExtensions.CompletedTask;
        }

        /// <summary>
        /// Processes the input value into a value that can be published to the UI thread.
        /// </summary>
        /// <param name="input">The value coming from the external dataflow blocks.</param>
        /// <param name="previousOutput">
        /// The value most recently obtained (and destined to be applied if not already) from a previous invocation of this method.
        /// </param>
        /// <returns>The value to apply to the UI thread.</returns>
        protected abstract Task<TOutput> PreprocessAsync(TInput input, TOutput previousOutput);

        /// <summary>
        /// Tests whether a given value should be published to the UI thread.
        /// </summary>
        /// <param name="previouslyAppliedOutput">The value most recently published to the UI thread.</param>
        /// <param name="newOutput">The new value that is a candidate for publishing to the UI thread.</param>
        /// <returns><c>true</c> to publish the value; <c>false</c> otherwise.</returns>
        protected virtual bool ShouldValueBeApplied(TOutput previouslyAppliedOutput, TOutput newOutput)
        {
            var equatable = previouslyAppliedOutput as IEquatable<TOutput>;
            if (equatable != null)
            {
                // By default, we only apply an update if it's not equivalent to the previous one.
                return !equatable.Equals(newOutput);
            }

            return true;
        }

        /// <summary>
        /// Applies the specified value on the UI thread so the host is aware of it.
        /// </summary>
        /// <param name="value">The value most recently produced by <see cref="PreprocessAsync"/></param>
        /// <remarks>
        /// Implementations should set the <see cref="AppliedValue"/> property in this method.
        /// </remarks>
        protected abstract Task ApplyAsync(TOutput value);

        /// <summary>
        /// Joins a set of data sources, arranging to disjoin them when this instance is disposed.
        /// </summary>
        protected void JoinUpstreamDataSources(params IJoinableProjectValueDataSource[] dataSources)
        {
            Requires.NotNull(dataSources, nameof(dataSources));
            disposableBag.AddDisposable(ProjectDataSources.JoinUpstreamDataSources(JoinableFactory, ProjectFaultHandlerService, dataSources));
        }
    }
}
