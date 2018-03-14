﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.VisualStudio.ProjectSystem
{
    /// <summary>
    ///     Provides properties for retrieving options for the project system.
    /// </summary>
    [ProjectSystemContract(ProjectSystemContractScope.ProjectService, ProjectSystemContractProvider.System)]
    internal interface IProjectSystemOptions
    {
        /// <summary>
        ///     Gets a value indicating if the project output pane is enabled.
        /// </summary>
        /// <value>
        ///     <see langword="true"/> if the project output pane is enabled; otherwise, <see langword="false"/>.
        /// </value>
        bool IsProjectOutputPaneEnabled
        {
            get;
        }

        /// <summary>
        ///     Gets a value indicating if the project fast up to date check is enabled.
        /// </summary>
        /// <value>
        ///     <see langword="true"/> if the project fast up to date check is enabled; otherwise, <see langword="false"/>
        /// </value>
        Task<bool> GetIsFastUpToDateCheckEnabledAsync();

        /// <summary>
        ///     Gets a value indicating the level of fast up to date check logging.
        /// </summary>
        /// <value>
        ///     The level of fast up to date check logging.
        /// </value>
        Task<LogLevel> GetFastUpToDateLoggingLevelAsync();
    }
}
