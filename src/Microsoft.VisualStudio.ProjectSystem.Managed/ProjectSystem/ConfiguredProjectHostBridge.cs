// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Threading;

namespace Microsoft.VisualStudio.ProjectSystem
{
    /// <summary>
    /// A configured project level service that bridges CPS Core snapshots to the VS UI thread.
    /// </summary>
    /// <typeparam name="TInput">The snapshot that gets processed for later publishing to the UI thread.</typeparam>
    /// <typeparam name="TOutput">The data that is used to apply changes to the UI thread.  A superset of <typeparamref name="TApplied"/>.</typeparam>
    /// <typeparam name="TApplied">The snapshot that gets published to the UI thread.  This type should be immutable.</typeparam>
    internal abstract class ConfiguredProjectHostBridge<TInput, TOutput, TApplied> : ProjectHostBridge<TInput, TOutput, TApplied>
        where TOutput : class
        where TApplied : class, IProjectValueVersions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfiguredProjectHostBridge{TInput, TOutput, TApplied}"/> class.
        /// </summary>
        protected ConfiguredProjectHostBridge(JoinableTaskContextNode joinableTaskContext)
            : base(joinableTaskContext)
        {
        }

        /// <summary>
        /// Gets exports from the active project configuration.
        /// </summary>
        [Import]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by MEF")]
        protected ConfiguredProject ConfiguredProject { get; private set; }

        /// <summary>
        /// Gets the CPS async task services.
        /// </summary>
        [Import(ExportContractNames.Scopes.ConfiguredProject)]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by MEF")]
        protected IProjectAsynchronousTasksService ConfiguredProjectAsynchronousTasksService { get; private set; }

        /// <summary>
        /// Gets the active configured project provider.
        /// </summary>
        [Import]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Called by MEF")]
        protected IActiveConfiguredProjectProvider ActiveConfiguredProjectProvider { get; private set; }

        /// <summary>
        /// Gets the CPS async task services.
        /// </summary>
        protected override IProjectAsynchronousTasksService ProjectAsynchronousTasksService
        {
            get { return ConfiguredProjectAsynchronousTasksService; }
        }

        /// <summary>
        /// Gets a value indicating whether new values are only forthcoming when this instance
        /// belongs to the active project configuration.
        /// </summary>
        protected virtual bool IsActiveConfigurationRequired
        {
            get { return false; }
        }

        /// <summary>
        /// Returns a task that will complete when a value that includes data that meets the specified requirements
        /// is published, and whose result will be the data about that value.
        /// </summary>
        /// <param name="minimumRequiredDataSourceVersions">The minimum required versions of various data sources that may be included in the value.</param>
        /// <param name="cancellationToken">A token that can signal lost interest in the published value.</param>
        /// <returns>A task whose result is data about the published value.</returns>
        protected internal async Task<TApplied> ApplyAsync(IImmutableDictionary<NamedIdentity, IProjectVersionRequirement> minimumRequiredDataSourceVersions, CancellationToken cancellationToken = default(CancellationToken))
        {
            Requires.NotNull(minimumRequiredDataSourceVersions, nameof(minimumRequiredDataSourceVersions));
            if (!cancellationToken.CanBeCanceled)
            {
                cancellationToken = ProjectAsynchronousTasksService.UnloadCancellationToken;
            }

            await InitializeAsync(cancellationToken);

            // We may need to assist in getting the value published to the UI thread (if our caller is
            // on the UI thread and will ultimately synchronously block on this).
            using (JoinableCollection.Join())
            {
                CancellationTokenSource cts = null;
                if (IsActiveConfigurationRequired)
                {
                    // What we're doing is sensitive to project config changes that occur on the UI thread,
                    // so mitigate race conditions by switch to the UI thread now.
                    await ThreadingService.SwitchToUIThread();
                    if (ActiveConfiguredProjectProvider.ActiveConfiguredProject != ConfiguredProject)
                    {
                        throw new OperationCanceledException();
                    }

                    cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ActiveConfiguredProjectProvider.ConfigurationActiveCancellationToken);
                    cancellationToken = cts.Token;
                }

                using (cts)
                {
                    var matchingValue = await AppliedValueBlock.GetSpecificVersionAsync(
                        value =>
                        {
                            if (minimumRequiredDataSourceVersions.IsSatisfiedBy(value))
                            {
                                return true;
                            }

                            TraceUtilities.TraceVerbose(
                                    @"Awaiting {2} update.
    Requirements:
    {0}
    Actuals:
    {1}",
                                minimumRequiredDataSourceVersions,
                                value.DataSourceVersions,
                                GetType().Name);

                            return false;
                        },
                        ConfiguredProject.Services,
                        cancellationToken);

                    return matchingValue;
                }
            }
        }
    }
}
