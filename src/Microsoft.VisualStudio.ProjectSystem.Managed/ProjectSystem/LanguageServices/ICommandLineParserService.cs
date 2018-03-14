﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.VisualStudio.ProjectSystem.LanguageServices
{
    /// <summary>
    ///     Parses <see cref="BuildOptions"/> instances from string-based command-line arguments.
    /// </summary>
    [ProjectSystemContract(ProjectSystemContractScope.UnconfiguredProject, ProjectSystemContractProvider.System)]
    internal interface ICommandLineParserService
    {
        /// <summary>
        ///     Parses the specified string-based command-line arguments.
        /// </summary>
        /// <param name="arguments">
        ///     A <see cref="IEnumerable{T}"/> of <see cref="string"/> representing the individual command-line
        ///     arguments.
        /// </param>
        /// <returns>
        ///     An <see cref="BuildOptions"/> representing the result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="arguments"/> is <see langword="null"/>.
        /// </exception>
        BuildOptions Parse(IEnumerable<string> arguments);
    }
}
