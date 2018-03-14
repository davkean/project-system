// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;

using Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.Subscriptions;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.CrossTarget
{

    [ProjectSystemContract(ProjectSystemContractScope.UnconfiguredProject, ProjectSystemContractProvider.System, ContractName = DependencySubscriptionsHost.DependencySubscriptionsHostContract)]
    internal interface ICrossTargetSubscriptionsHost
    {
        Task<AggregateCrossTargetProjectContext> GetCurrentAggregateProjectContext();
        Task<ConfiguredProject> GetConfiguredProject(ITargetFramework target);
    }
}
