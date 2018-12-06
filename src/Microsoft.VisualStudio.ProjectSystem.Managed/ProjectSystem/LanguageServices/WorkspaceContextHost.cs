﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.LanguageServices.ProjectSystem;

namespace Microsoft.VisualStudio.ProjectSystem.LanguageServices
{
    /// <summary>
    ///     Responsible for creating and initializing <see cref="IWorkspaceProjectContext"/> and sending 
    ///     on changes to the project to the <see cref="IApplyChangesToWorkspaceContext"/> service.
    /// </summary>
    [Export(typeof(IImplicitlyActiveService))]
    [Export(typeof(IWorkspaceProjectContextHost))]
    [AppliesTo(ProjectCapability.DotNetLanguageService2)]
    internal partial class WorkspaceContextHost : AbstractMultiLifetimeComponent<WorkspaceContextHost.WorkspaceContextHostInstance>, IImplicitlyActiveService, IWorkspaceProjectContextHost
    {
        private readonly ConfiguredProject _project;
        private readonly IProjectThreadingService _threadingService;
        private readonly IUnconfiguredProjectTasksService _tasksService;
        private readonly IProjectSubscriptionService _projectSubscriptionService;
        private readonly IWorkspaceProjectContextProvider _workspaceProjectContextProvider;
        private readonly IActiveEditorContextTracker _activeWorkspaceProjectContextTracker;
        private readonly ExportFactory<IApplyChangesToWorkspaceContext> _applyChangesToWorkspaceContextFactory;

        [ImportingConstructor]
        public WorkspaceContextHost(ConfiguredProject project,
                                    IProjectThreadingService threadingService,
                                    IUnconfiguredProjectTasksService tasksService,
                                    IProjectSubscriptionService projectSubscriptionService,
                                    IWorkspaceProjectContextProvider workspaceProjectContextProvider,
                                    IActiveEditorContextTracker activeWorkspaceProjectContextTracker,
                                    ExportFactory<IApplyChangesToWorkspaceContext> applyChangesToWorkspaceContextFactory)
            : base(threadingService.JoinableTaskContext)
        {
            _project = project;
            _threadingService = threadingService;
            _tasksService = tasksService;
            _projectSubscriptionService = projectSubscriptionService;
            _workspaceProjectContextProvider = workspaceProjectContextProvider;
            _activeWorkspaceProjectContextTracker = activeWorkspaceProjectContextTracker;
            _applyChangesToWorkspaceContextFactory = applyChangesToWorkspaceContextFactory;
        }

        public Task ActivateAsync()
        {
            return LoadAsync();
        }

        public Task DeactivateAsync()
        {
            return UnloadAsync();
        }

        public Task PublishAsync(CancellationToken cancellationToken = default)
        {
            return WaitForLoadedAsync(cancellationToken);
        }

        public async Task OpenContextForWriteAsync(Func<IWorkspaceProjectContextAccessor, Task> action)
        {
            Requires.NotNull(action, nameof(action));

            WorkspaceContextHostInstance instance = await WaitForLoadedAsync();

            // Throws ActiveProjectConfigurationChangedException if 'instance' is Disposed
            await instance.OpenContextForWriteAsync(action);
        }

        public async Task<T> OpenContextForWriteAsync<T>(Func<IWorkspaceProjectContextAccessor, Task<T>> action)
        {
            Requires.NotNull(action, nameof(action));

            WorkspaceContextHostInstance instance = await WaitForLoadedAsync();

            // Throws ActiveProjectConfigurationChangedException if 'instance' is Disposed
            return await instance.OpenContextForWriteAsync(action);
        }

        protected override WorkspaceContextHostInstance CreateInstance()
        {
            return new WorkspaceContextHostInstance(_project, _threadingService, _tasksService, _projectSubscriptionService, _workspaceProjectContextProvider, _activeWorkspaceProjectContextTracker, _applyChangesToWorkspaceContextFactory);
        }
    }
}
