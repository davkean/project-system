﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;

using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;

[assembly: ProjectSystemContract(ProjectSystemContractScope.ConfiguredProject, ProjectSystemContractProvider.Extension, ContractName = "UserFileWithXamlDefaultsWithInterception", ContractType = typeof(IProjectPropertiesProvider))]
[assembly: ProjectSystemContract(ProjectSystemContractScope.ConfiguredProject, ProjectSystemContractProvider.Extension, ContractName = "UserFileWithXamlDefaultsWithInterception", ContractType = typeof(IProjectInstancePropertiesProvider))]

namespace Microsoft.VisualStudio.ProjectSystem.Properties
{
    [Export("UserFileWithXamlDefaultsWithInterception", typeof(IProjectPropertiesProvider))]
    [Export(typeof(IProjectPropertiesProvider))]
    [Export("UserFileWithXamlDefaultsWithInterception", typeof(IProjectInstancePropertiesProvider))]
    [Export(typeof(IProjectInstancePropertiesProvider))]
    [ExportMetadata("Name", "UserFileWithXamlDefaultsWithInterception")]
    [AppliesTo(ProjectCapability.CSharpOrVisualBasicOrFSharp)]
    internal sealed class UserFileWithXamlDefaultsInterceptedProjectPropertiesProvider : UserFileInterceptedProjectPropertiesProvider
    {
        [ImportingConstructor]
        public UserFileWithXamlDefaultsInterceptedProjectPropertiesProvider(
            [Import(ContractNames.ProjectPropertyProviders.UserFileWithXamlDefaults)] IProjectPropertiesProvider provider,
            // We use project file here because in CPS, the UserFileWithXamlDefaults instance provider is implemented by the same
            // provider as the ProjectFile, and is exported as the ProjectFile provider.
            [Import(ContractNames.ProjectPropertyProviders.ProjectFile)] IProjectInstancePropertiesProvider instanceProvider,
            UnconfiguredProject unconfiguredProject,
            [ImportMany(ContractNames.ProjectPropertyProviders.UserFileWithXamlDefaults)]IEnumerable<Lazy<IInterceptingPropertyValueProvider, IInterceptingPropertyValueProviderMetadata>> interceptingValueProviders)
            : base(provider, instanceProvider, unconfiguredProject, interceptingValueProviders)
        {
        }
    }
}
