// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using NuGet.SolutionRestoreManager;

namespace Microsoft.VisualStudio.ProjectSystem.VS.NuGet
{
    /// <summary>
    ///     Represents the data source of metadata needed for restore operations for a <see cref="UnconfiguredProject"/>
    ///     instance by resolving conflicts and combining the data of all implicitly active <see cref="ConfiguredProject"/> 
    ///     instances.
    /// </summary>
    internal interface IPackageRestoreUnconfiguredDataSource : IProjectValueDataSource<IVsProjectRestoreInfo>
    {
    }
}
