// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServices.ProjectSystem;

namespace Microsoft.VisualStudio.ProjectSystem.LanguageServices
{
    /// <summary>
    ///     Temporary class to bridge the two language service integrations; LanguageServiceHost/IWorkspaceProjectContextHost so that 
    ///     consumors of <see cref="ILanguageServiceHost"/> do not need to understand the undercovers which integration is being used.
    /// </summary>
    [Export(typeof(IActiveWorkspaceProjectContextHost))]
    internal class LanguageServiceBridge : IActiveWorkspaceProjectContextHost
    {
        private readonly UnconfiguredProject _project;
        private readonly LanguageServiceHost _languageServiceHost;
        private readonly ActiveConfiguredProject<IWorkspaceProjectContextHost> _activeProjectContextHost;

        [ImportingConstructor]
        public LanguageServiceBridge(UnconfiguredProject project, LanguageServiceHost languageServiceHost, ActiveConfiguredProject<IWorkspaceProjectContextHost> activeProjectContextHost)
        {
            _project = project;
            _languageServiceHost = languageServiceHost;
            _activeProjectContextHost = activeProjectContextHost;
        }

        public ConfiguredProject ConfiguredProject
        {
            get;
        }

        public Task Initialized
        {
            get
            {
                if (IsLanguageService2())
                {
                    return _activeProjectContextHost.Value.Initialized;
                }

                return _languageServiceHost.InitializeAsync();
            }
        }

        public Task OpenContextForRead(Func<IWorkspaceProjectContext, Task> action)
        {
            if (IsLanguageService2())
            {
                return _activeProjectContextHost.Value.OpenContextForRead(action);
            }

            return _languageServiceHost.OpenContextForRead(action);
        }

        private bool IsLanguageService2()
        {
            return _project.Capabilities.Contains(ProjectCapability.LanguageService2);
        }
    }
}
