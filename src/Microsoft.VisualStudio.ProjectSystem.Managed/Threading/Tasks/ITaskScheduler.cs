// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;

using Microsoft.VisualStudio.ProjectSystem;

namespace Microsoft.VisualStudio.Threading.Tasks
{
    /// <summary>
    ///     Schedules asynchronous tasks.
    /// </summary>
    [ProjectSystemContract(ProjectSystemContractScope.ProjectService, ProjectSystemContractProvider.System)]
    internal interface ITaskScheduler
    {
        JoinableTask<T> RunAsync<T>(TaskSchedulerPriority priority, Func<System.Threading.Tasks.Task<T>> asyncMethod);
    }
}
