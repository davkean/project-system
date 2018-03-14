﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;

using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Logging
{
    /// <summary>
    ///     Provides access to the project output window pane.
    /// </summary>
    [ProjectSystemContract(ProjectSystemContractScope.ProjectService, ProjectSystemContractProvider.System)]
    internal interface IProjectOutputWindowPaneProvider
    {
        /// <summary>
        ///     Returns the project output window pane.
        /// </summary>
        /// <returns>
        ///     The project <see cref="IVsOutputWindowPane"/> object, or <see langword="null"/> 
        ///     if the <see cref="IVsOutputWindow"/> service is not present.
        /// </returns>
        Task<IVsOutputWindowPane> GetOutputWindowPaneAsync();
    }
}
