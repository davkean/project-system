// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.VisualStudio.ProjectSystem
{
    /// <summary>
    ///     Provides an implementation of <see cref="OnceInitializedOnceDisposedAsync"/> that lets 
    ///     implementors protect themselves from being disposed while doing work.
    /// </summary>
    /// <remarks>
    ///     <see cref="OnceInitializedOnceDisposed"/> lets implementors prevent themselves from being disposed
    ///     by locking <see cref="OnceInitializedOnceDisposed.SyncObject"/>. This class provides a similar 
    ///     mechanism by passing a delegate into <see cref="RunWithinLockAsync"/>.
    /// </remarks>
    internal abstract class ThreadSafeOnceInitializedOnceDisposedAsync : OnceInitializedOnceDisposedAsync
    {
        private readonly ReentrantSemaphore _semaphore; 

        protected ThreadSafeOnceInitializedOnceDisposedAsync(JoinableTaskContextNode joinableTaskContextNode)
            : base(joinableTaskContextNode)
        {
            _semaphore = ReentrantSemaphore.Create(1, joinableTaskContextNode.Context, ReentrantSemaphore.ReentrancyMode.Stack);
        }

        protected override sealed async Task DisposeCoreAsync(bool initialized)
        {
            await RunWithinLockAsync(ct => DisposeWithinLockAsync(initialized), CancellationToken.None).ConfigureAwait(true);

            _semaphore.Dispose();
        }

        protected abstract Task DisposeWithinLockAsync(bool initialized);

        protected Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            return _semaphore.ExecuteAsync(action, cancellationToken);
        }

        protected Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            var result = default(T);

            return (Task<T>)_semaphore.ExecuteAsync(action, cancellationToken);
        }

        protected async Task<T> RunWithinLockAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            // Join the caller to our collection, so that if the lock is already held by another task that needs UI 
            // thread access we don't deadlock if we're also being waited on by the UI thread. For example, when CPS
            // is draining critical tasks and is waiting us.
            using (JoinableCollection.Join())
            {
                using (await _semaphore.EnterAsync(cancellationToken).ConfigureAwait(true))
                {
                    // We do an inner JoinableTaskFactory.RunAsync here to workaround
                    // https://github.com/Microsoft/vs-threading/issues/132
                    JoinableTask<T> joinableTask = JoinableFactory.RunAsync(() => action(cancellationToken));

                    return await joinableTask.Task
                                             .ConfigureAwait(true);
                }
            }
        }
    }
}
