// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;

using EnvDTE;

using Microsoft.VisualStudio.LanguageServices.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.LanguageServices;

namespace Microsoft.VisualStudio.ProjectSystem.VS.LanguageServices
{
    /// <summary>
    ///     Adapts CPS's <see cref="ICodeModelProvider"/> and <see cref="IProjectCodeModelProvider"/> to Roslyn's <see cref="ICodeModelFactory"/> implementation.
    /// </summary>
    [Export(typeof(ICodeModelProvider))]
    [Export(typeof(IProjectCodeModelProvider))]
    [AppliesTo(ProjectCapability.DotNetLanguageService2)]
    internal class ProjectContextCodeModelProvider2 : ICodeModelProvider, IProjectCodeModelProvider
    {
        private readonly IProjectThreadingService _threadingService;
        private readonly IProjectTreeService _projectTreeService;
        private readonly Lazy<ICodeModelFactory> _codeModelFactory;
        private readonly IWorkspaceProjectContextService _workspaceProjectContextService;
        private readonly ILanguageServiceHost _languageServiceHost;

        [ImportingConstructor]
        public ProjectContextCodeModelProvider2(IProjectThreadingService threadingService,
                                                [Import(ExportContractNames.ProjectTreeProviders.PhysicalProjectTreeService)]IProjectTreeService projectTreeService,
                                                IWorkspaceProjectContextService workspaceProjectContextService,
                                                Lazy<ICodeModelFactory> codeModelFactory)       // From Roslyn
        {
            _threadingService = threadingService;
            _projectTreeService = projectTreeService;
            _codeModelFactory = codeModelFactory;
            _workspaceProjectContextService = workspaceProjectContextService;
        }

        public CodeModel GetCodeModel(Project project)
        {
            Requires.NotNull(project, nameof(project));

            return _threadingService.ExecuteSynchronously(async () =>
            {
                _projectTreeService.CurrentTree

                await _threadingService.SwitchToUIThread();

                return _codeModelFactory.Value.GetCodeModel(projectContext, project);
            });
        }

        public FileCodeModel GetFileCodeModel(ProjectItem fileItem)
        {
            Requires.NotNull(fileItem, nameof(fileItem));

            IWorkspaceProjectContext projectContext = _languageServiceHost.ActiveProjectContext;
            if (projectContext == null)
                return null;

            return _threadingService.ExecuteSynchronously(async () =>
            {
                await _threadingService.SwitchToUIThread();

                try
                {
                    return _codeModelFactory.Value.GetFileCodeModel(projectContext, fileItem);
                }
                catch (NotImplementedException)
                {   // Isn't a file that Roslyn knows about
                }

                return null;
            });
        }
    }
}
