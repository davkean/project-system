// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.
using System;

namespace Microsoft.VisualStudio.ProjectSystem.VS.PropertyPages
{
    [ProjectSystemContract(ProjectSystemContractScope.UnconfiguredProject, ProjectSystemContractProvider.Extension)]
    public interface IPageMetadata
    {
        bool HasConfigurationCondition { get; }
        string Name { get; }
        Guid PageGuid { get; }
        int PageOrder { get; }
    }
}
