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
    [AppliesTo(ProjectCapability.DotNetLanguageServiceOrLanguageService2)]
    internal class ProjectContextCodeModelProvider : ICodeModelProvider, IProjectCodeModelProvider
    {
        private readonly IProjectThreadingService _threadingService;
        private readonly ICodeModelFactory _codeModelFactory;
        private readonly IActiveWorkspaceProjectContextHost _projectContextHost;

        [ImportingConstructor]
        public ProjectContextCodeModelProvider(IProjectThreadingService threadingService, ICodeModelFactory codeModelFactory, IActiveWorkspaceProjectContextHost projectContextHost)
        {
            _threadingService = threadingService;
            _codeModelFactory = codeModelFactory;
            _projectContextHost = projectContextHost;
        }

        public CodeModel GetCodeModel(Project project)
        {
            Requires.NotNull(project, nameof(project));

            return Invoke(context => _codeModelFactory.GetCodeModel(context, project));
        }

        public FileCodeModel GetFileCodeModel(ProjectItem fileItem)
        {
            Requires.NotNull(fileItem, nameof(fileItem));

            return Invoke(context => 
            {
                try
                {
                    return _codeModelFactory.GetFileCodeModel(context, fileItem);
                }
                catch (NotImplementedException)
                {   // Not a file Roslyn knows about
                }

                return null;
            });
        }

        private T Invoke<T>(Func<IWorkspaceProjectContext, T> action)
        {
            return _threadingService.ExecuteSynchronously(async () =>
            {
                T result = default;

                try
                {
                    await _projectContextHost.OpenContextForWriteAsync(async context =>
                    {
                        // Explicitly switch to UI thread because ICodeModelFactory produces STA-bound objects
                        await _threadingService.SwitchToUIThread();

                        result = action(context);
                    });
                }
                catch (OperationCanceledException)
                {   // No context created, or project unloading
                }

                return result;
            });
        }
    }
}
