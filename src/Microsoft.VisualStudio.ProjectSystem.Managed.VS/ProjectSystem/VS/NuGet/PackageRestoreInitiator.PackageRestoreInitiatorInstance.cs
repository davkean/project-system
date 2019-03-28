﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Internal.Performance;
using Microsoft.VisualStudio.ProjectSystem.Logging;
using Microsoft.VisualStudio.Threading;

using NuGet.SolutionRestoreManager;

namespace Microsoft.VisualStudio.ProjectSystem.VS.NuGet
{
    internal partial class PackageRestoreInitiator
    {
        internal class PackageRestoreInitiatorInstance : OnceInitializedOnceDisposedAsync, IMultiLifetimeInstance
        {
            private readonly UnconfiguredProject _project;
            private readonly IPackageRestoreUnconfiguredDataSource _dataSource;
            private readonly IProjectAsynchronousTasksService _projectAsynchronousTasksService;
            private readonly IUnconfiguredProjectVsServices _projectVsServices;
            private readonly IVsSolutionRestoreService _solutionRestoreService;
            private readonly IProjectLogger _logger;

            private IDisposable _subscription;

            public PackageRestoreInitiatorInstance(UnconfiguredProject project,
                                                   IPackageRestoreUnconfiguredDataSource dataSource,
                                                   IProjectThreadingService threadingService,
                                                   IProjectAsynchronousTasksService projectAsynchronousTasksService,
                                                   IVsSolutionRestoreService solutionRestoreService,
                                                   IProjectLogger logger)
                : base(threadingService.JoinableTaskContext)
            {
                _project = project;
                _dataSource = dataSource;
                _projectAsynchronousTasksService = projectAsynchronousTasksService;
                _solutionRestoreService = solutionRestoreService;
                _logger = logger;
            }

            public Task InitializeAsync()
            {
                return InitializeAsync(CancellationToken.None);
            }

            protected override Task InitializeCoreAsync(CancellationToken cancellationToken)
            {
                _subscription = _dataSource.SourceBlock.LinkToAsyncAction(OnProjectRestoreInfoChanged);

                return Task.CompletedTask;
            }

            protected override Task DisposeCoreAsync(bool initialized)
            {
                _subscription?.Dispose();

                return Task.CompletedTask;
            }

            private async Task OnProjectRestoreInfoChanged(IProjectVersionedValue<IVsProjectRestoreInfo> e)
            {
                JoinableTask joinableTask = JoinableFactory.RunAsync(() => {

                    return NominateProjectRestoreAsync(e.Value, _projectAsynchronousTasksService.UnloadCancellationToken);
                });

                _projectAsynchronousTasksService.RegisterAsyncTask(joinableTask,
                                                                   ProjectCriticalOperation.Build | ProjectCriticalOperation.Unload | ProjectCriticalOperation.Rename,
                                                                   registerFaultHandler: true);

                // Prevent overlap until Restore completes
                await joinableTask;
            }

            private async Task NominateProjectRestoreAsync(IVsProjectRestoreInfo restoreInfo, CancellationToken cancellationToken)
            {
                RestoreLogger.BeginNominateRestore(_logger, _projectVsServices.Project.FullPath, restoreInfo);

                // Nominate NuGet with the restore data. This will complete when we're guaranteed 
                // that the  assets files *at least* contains the changes that we pushed to it.
                await _solutionRestoreService.NominateProjectAsync(_project.FullPath, restoreInfo, cancellationToken);

                CodeMarkers.Instance.CodeMarker(CodeMarkerTimerId.PerfPackageRestoreEnd);

                RestoreLogger.EndNominateRestore(_logger, _project.FullPath);
            }
        }
    }
}
