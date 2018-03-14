﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.ProjectSystem
{
    /// <summary>
    ///     Provides a mechanism to safely access the project GUID. Replaces usage of <see cref="IProjectGuidService"/> 
    ///     and <see cref="IProjectGuidService2"/>.
    /// </summary>
    /// <remarks>
    ///     <see cref="IProjectGuidService"/> and <see cref="IProjectGuidService2"/> will retrieve the project GUID of 
    ///     the project *at the time* that it is called. During project initialization, the GUID may be changed by the 
    ///     solution in reaction to a clash with another project. <see cref="ISafeProjectGuidService"/> will wait until
    ///     it is safe to retrieve the project GUID before returning it.
    /// </remarks>
    [ProjectSystemContract(ProjectSystemContractScope.UnconfiguredProject, ProjectSystemContractProvider.System)]
    internal interface ISafeProjectGuidService
    {
        /// <summary>
        ///     Returns the project GUID, waiting until project load has safely progressed 
        ///     to a point where the GUID is guaranteed not to change.
        /// </summary>
        /// <returns>
        ///     The GUID of the current project.
        /// </returns>
        /// <exception cref="OperationCanceledException">
        ///     The project was unloaded before project load had finished.
        /// </exception>
        Task<Guid> GetProjectGuidAsync();
    }
}
