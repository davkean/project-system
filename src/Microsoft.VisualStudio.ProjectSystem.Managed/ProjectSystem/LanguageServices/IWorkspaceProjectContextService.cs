// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.ProjectSystem.LanguageServices
{
    internal interface IWorkspaceProjectContextService
    {
        IWorkspaceProjectContextServiceState Current
        {
            get;
        }

        Task<IWorkspaceProjectContextServiceState> PublishAsync(IImmutableDictionary<NamedIdentity, IProjectVersionRequirement> minimumRequiredDataSourceVersions, CancellationToken cancellationToken = default);

        Task<IWorkspaceProjectContextServiceState> PublishProjectEvaluation(IImmutableDictionary<NamedIdentity, IProjectVersionRequirement> minimumRequiredDataSourceVersions, CancellationToken cancellationToken = default);
    }
}
