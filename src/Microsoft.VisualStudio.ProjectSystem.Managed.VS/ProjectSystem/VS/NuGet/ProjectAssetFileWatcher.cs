﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.Utilities;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Threading.Tasks;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.ProjectSystem.VS.NuGet
{
    /// <summary>
    ///     Watches for writes to the project.assets.json, triggering a evaluation if it changes.
    /// </summary>
    internal class ProjectAssetFileWatcher : OnceInitializedOnceDisposedAsync, IVsFreeThreadedFileChangeEvents2
    {
        private static readonly TimeSpan s_notifyDelay = TimeSpan.FromMilliseconds(100);

        private readonly IVsService<IVsAsyncFileChangeEx> _fileChangeService;
        private readonly IUnconfiguredProjectCommonServices _projectServices;
        private readonly IUnconfiguredProjectTasksService _projectTasksService;
        private readonly IActiveConfiguredProjectSubscriptionService _activeConfiguredProjectSubscriptionService;
        private readonly IProjectTreeProvider _fileSystemTreeProvider;

        private CancellationTokenSource _watchedFileResetCancellationToken;
        private ITaskDelayScheduler _taskDelayScheduler;
        private IDisposable _treeWatcher;
        private uint _filechangeCookie;
        private string _fileBeingWatched;
        private byte[] _previousContentsHash;

        [ImportingConstructor]
        public ProjectAssetFileWatcher(
            IVsService<SVsFileChangeEx, IVsAsyncFileChangeEx> fileChangeService,
            [Import(ContractNames.ProjectTreeProviders.FileSystemDirectoryTree)] IProjectTreeProvider fileSystemTreeProvider,
            IUnconfiguredProjectCommonServices projectServices,
            IUnconfiguredProjectTasksService projectTasksService,
            IActiveConfiguredProjectSubscriptionService activeConfiguredProjectSubscriptionService)
            : base(projectServices.ThreadingService.JoinableTaskContext)
        {
            Requires.NotNull(fileChangeService, nameof(fileChangeService));
            Requires.NotNull(fileSystemTreeProvider, nameof(fileSystemTreeProvider));
            Requires.NotNull(projectServices, nameof(projectServices));
            Requires.NotNull(projectTasksService, nameof(projectTasksService));
            Requires.NotNull(activeConfiguredProjectSubscriptionService, nameof(activeConfiguredProjectSubscriptionService));

            _fileChangeService = fileChangeService;
            _fileSystemTreeProvider = fileSystemTreeProvider;
            _projectServices = projectServices;
            _projectTasksService = projectTasksService;
            _activeConfiguredProjectSubscriptionService = activeConfiguredProjectSubscriptionService;
        }

        /// <summary>
        /// Called on project load.
        /// </summary>
#pragma warning disable RS0030 // symbol ConfiguredProjectAutoLoad is banned
        [ConfiguredProjectAutoLoad(RequiresUIThread = false)]
#pragma warning restore RS0030 // symbol ConfiguredProjectAutoLoad is banned
        [AppliesTo(ProjectCapability.DotNet)]
        internal void Load()
        {
            InitializeAsync();
        }

        /// <summary>
        /// Called on changes to the project tree.
        /// </summary>
        internal async Task DataFlow_ChangedAsync(IProjectVersionedValue<Tuple<IProjectTreeSnapshot, IProjectSubscriptionUpdate>> dataFlowUpdate)
        {
            await InitializeAsync();

            IProjectTreeSnapshot treeSnapshot = dataFlowUpdate.Value.Item1;
            IProjectTree newTree = treeSnapshot.Tree;
            if (newTree == null)
            {
                return;
            }

            // If tree changed when we are disposing then ignore the change.
            if (IsDisposing)
            {
                return;
            }

            // NOTE: Project lock file path may be null
            IProjectSubscriptionUpdate projectUpdate = dataFlowUpdate.Value.Item2;
            string projectLockFilePath = GetProjectAssetsFilePath(newTree, projectUpdate);

            // project.json may have been renamed to {projectName}.project.json or in the case of the project.assets.json,
            // the immediate path could have changed. In either case, change the file watcher.
            if (!PathHelper.IsSamePath(projectLockFilePath, _fileBeingWatched))
            {
                await UnregisterFileWatcherIfAnyAsync();
                await RegisterFileWatcherAsync(projectLockFilePath);
            }
        }

        /// <summary>
        /// Initialize the watcher.
        /// </summary>
        protected override async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            // Explicitly get back to the thread pool for the rest of this method so we don't tie up the UI thread;
            await TaskScheduler.Default;

            await _projectTasksService.LoadedProjectAsync(() =>
                {
                    // The tree source to get changes to the tree so that we can identify when the assets file changes.
                    ProjectDataSources.SourceBlockAndLink<IProjectVersionedValue<IProjectTreeSnapshot>> treeSource = _fileSystemTreeProvider.Tree.SyncLinkOptions();

                    // The property source used to get the value of the $ProjectAssetsFile property so that we can identify the location of the assets file.
                    StandardRuleDataflowLinkOptions sourceLinkOptions = DataflowOption.WithRuleNames(ConfigurationGeneral.SchemaName);

                    ProjectDataSources.SourceBlockAndLink<IProjectVersionedValue<IProjectSubscriptionUpdate>> propertySource = _activeConfiguredProjectSubscriptionService.ProjectRuleSource.SourceBlock.SyncLinkOptions(sourceLinkOptions);
                    System.Threading.Tasks.Dataflow.ITargetBlock<IProjectVersionedValue<Tuple<IProjectTreeSnapshot, IProjectSubscriptionUpdate>>> target = DataflowBlockSlim.CreateActionBlock<IProjectVersionedValue<Tuple<IProjectTreeSnapshot, IProjectSubscriptionUpdate>>>(DataFlow_ChangedAsync);

                    // Join the two sources so that we get synchronized versions of the data.
                    _treeWatcher = ProjectDataSources.SyncLinkTo(treeSource, propertySource, target);

                    return Task.CompletedTask;
                });
        }

        protected override async Task DisposeCoreAsync(bool initialized)
        {
            if (initialized)
            {
                _taskDelayScheduler?.Dispose();
                _treeWatcher.Dispose();
                await UnregisterFileWatcherIfAnyAsync();
            }
        }

        private static byte[] GetFileHashOrNull(string path)
        {
            byte[] hash = null;

            try
            {
                using (var hasher = SHA256.Create())
                using (FileStream file = File.OpenRead(path))
                {
                    file.Position = 0;
                    hash = hasher.ComputeHash(file);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return hash;
        }

        private static string GetProjectAssetsFilePath(IProjectTree newTree, IProjectSubscriptionUpdate projectUpdate)
        {
            string projectFilePath = projectUpdate.CurrentState.GetPropertyOrDefault(ConfigurationGeneral.SchemaName, ConfigurationGeneral.MSBuildProjectFullPathProperty, null);

            // First check to see if the project has a project.json.
            IProjectTree projectJsonNode = FindProjectJsonNode(newTree, projectFilePath);
            if (projectJsonNode != null)
            {
                string projectDirectory = Path.GetDirectoryName(projectFilePath);
                string projectLockJsonFilePath = Path.ChangeExtension(PathHelper.Combine(projectDirectory, projectJsonNode.Caption), ".lock.json");
                return projectLockJsonFilePath;
            }

            // If there is no project.json then get the patch to obj\project.assets.json file which is generated for projects
            // with <PackageReference> items.
            string objDirectory = projectUpdate.CurrentState.GetPropertyOrDefault(ConfigurationGeneral.SchemaName, ConfigurationGeneral.BaseIntermediateOutputPathProperty, null);

            if (string.IsNullOrEmpty(objDirectory))
            {
                // Don't have an intermediate directory set, probably missing SDK attribute or Microsoft.Common.props
                return null;
            }

            objDirectory = PathHelper.MakeRooted(projectFilePath, objDirectory);
            string projectAssetsFilePath = PathHelper.Combine(objDirectory, "project.assets.json");
            return projectAssetsFilePath;
        }

        private static IProjectTree FindProjectJsonNode(IProjectTree newTree, string projectFilePath)
        {
            if (newTree.TryFindImmediateChild("project.json", out IProjectTree projectJsonNode))
            {
                return projectJsonNode;
            }

            string projectName = Path.GetFileNameWithoutExtension(projectFilePath);
            if (newTree.TryFindImmediateChild($"{projectName}.project.json", out projectJsonNode))
            {
                return projectJsonNode;
            }

            return null;
        }

        private async Task RegisterFileWatcherAsync(string projectLockJsonFilePath)
        {
            // Note file change service is free-threaded
            if (projectLockJsonFilePath != null)
            {
                _previousContentsHash = GetFileHashOrNull(projectLockJsonFilePath);
                _taskDelayScheduler?.Dispose();
                _taskDelayScheduler = new TaskDelayScheduler(
                    s_notifyDelay,
                    _projectServices.ThreadingService,
                    CreateLinkedCancellationToken());

                IVsAsyncFileChangeEx fileChangeService = await _fileChangeService.GetValueAsync();
                _filechangeCookie = await fileChangeService.AdviseFileChangeAsync(
                    projectLockJsonFilePath,
                    _VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Size | _VSFILECHANGEFLAGS.VSFILECHG_Add | _VSFILECHANGEFLAGS.VSFILECHG_Del,
                    sink: this);
            }

            _fileBeingWatched = projectLockJsonFilePath;
        }

        private async Task UnregisterFileWatcherIfAnyAsync()
        {
            // Note file change service is free-threaded
            if (_filechangeCookie != VSConstants.VSCOOKIE_NIL)
            {
                IVsAsyncFileChangeEx fileChangeService = await _fileChangeService.GetValueAsync();

                // There's nothing for us to do if this fails. So ignore the return value.
                await fileChangeService.UnadviseFileChangeAsync(_filechangeCookie);
                _watchedFileResetCancellationToken?.Cancel();
                _watchedFileResetCancellationToken?.Dispose();
                _taskDelayScheduler?.Dispose();
            }
        }

        private CancellationToken CreateLinkedCancellationToken()
        {
            // we want to cancel when we switch what file is watched, or when the project is unloaded
            if (_projectServices?.Project?.Services?.ProjectAsynchronousTasks?.UnloadCancellationToken != null)
            {
                _watchedFileResetCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(
                    _projectServices.Project.Services.ProjectAsynchronousTasks.UnloadCancellationToken);
            }
            else
            {
                _watchedFileResetCancellationToken = new CancellationTokenSource();
            }

            return _watchedFileResetCancellationToken.Token;
        }

        private async Task HandleFileChangedAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Only notify the project if the contents of the watched file have changed.
                // In the case if we fail to read the contents, we will opt to notify the project.
                byte[] newHash = GetFileHashOrNull(_fileBeingWatched);
                if (newHash == null || _previousContentsHash == null || !newHash.SequenceEqual(_previousContentsHash))
                {
                    TraceUtilities.TraceVerbose("{0} changed on disk. Marking project dirty", _fileBeingWatched);
                    _previousContentsHash = newHash;
                    cancellationToken.ThrowIfCancellationRequested();
                    await _projectServices.Project.Services.ProjectAsynchronousTasks.LoadedProjectAsync(async () =>
                    {
                        await _projectServices.ProjectAccessor.EnterWriteLockAsync(async (collection, token) =>
                        {
                            // notify all the loaded configured projects
                            IEnumerable<ConfiguredProject> currentProjects = _projectServices.Project.LoadedConfiguredProjects;
                            foreach (ConfiguredProject configuredProject in currentProjects)
                            {
                                await _projectServices.ProjectAccessor.OpenProjectForWriteAsync(configuredProject, project =>
                                {
                                    project.MarkDirty();
                                    configuredProject.NotifyProjectChange();

                                }, ProjectCheckoutOption.DoNotCheckout, cancellationToken); // Stay on same thread that took lock
                            }
                        }, cancellationToken); // Stay on same thread that took lock
                    });
                }
                else
                {
                    TraceUtilities.TraceWarning("{0} changed on disk, but has no actual content change.", _fileBeingWatched);
                }
            }
            catch (OperationCanceledException)
            {
                // Project is already unloaded
            }
        }

        /// <summary>
        /// Called when a project.lock.json file changes.
        /// </summary>
        public int FilesChanged(uint cChanges, string[] rgpszFile, uint[] rggrfChange)
        {
            // Kick off the operation to notify the project change in a different thread regardless of
            // the kind of change since we are interested in all changes.
            _taskDelayScheduler.ScheduleAsyncTask(HandleFileChangedAsync);

            return VSConstants.S_OK;
        }

        public int DirectoryChanged(string pszDirectory)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int DirectoryChangedEx(string pszDirectory, string pszFile)
        {
            return VSConstants.E_NOTIMPL;
        }

        public int DirectoryChangedEx2(string pszDirectory, uint cChanges, string[] rgpszFile, uint[] rggrfChange)
        {
            return VSConstants.E_NOTIMPL;
        }
    }
}
