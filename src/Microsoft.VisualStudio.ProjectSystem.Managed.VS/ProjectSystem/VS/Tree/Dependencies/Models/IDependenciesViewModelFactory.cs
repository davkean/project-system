﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.Snapshot;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.Models
{
    [ProjectSystemContract(ProjectSystemContractScope.UnconfiguredProject, ProjectSystemContractProvider.System)]
    internal interface IDependenciesViewModelFactory
    {
        IDependencyViewModel CreateTargetViewModel(ITargetedDependenciesSnapshot snapshot);
        IDependencyViewModel CreateRootViewModel(string providerType, bool hasUnresolvedDependency);
        ImageMoniker GetDependenciesRootIcon(bool hasUnresolvedDependencies);
    }
}
