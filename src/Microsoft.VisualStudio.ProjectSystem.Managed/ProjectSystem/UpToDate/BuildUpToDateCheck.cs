﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Telemetry;

namespace Microsoft.VisualStudio.ProjectSystem.UpToDate
{
    [AppliesTo(ProjectCapability.CSharpOrVisualBasicOrFSharp + "+ !" + ProjectCapabilities.SharedAssetsProject)]
    [Export(typeof(IBuildUpToDateCheckProvider))]
    [ExportMetadata("BeforeDrainCriticalTasks", true)]
    internal sealed class BuildUpToDateCheck : OnceInitializedOnceDisposed, IBuildUpToDateCheckProvider
    {
        private const string CopyToOutputDirectory = "CopyToOutputDirectory";
        private const string PreserveNewest = "PreserveNewest";
        private const string Always = "Always";
        private const string TelemetryEventName = "UpToDateCheck";
        private const string Link = "Link";

        private static ImmutableHashSet<string> ReferenceSchemas => ImmutableStringHashSet.EmptyOrdinal
            .Add(ResolvedAnalyzerReference.SchemaName)
            .Add(ResolvedCompilationReference.SchemaName);

        private static ImmutableHashSet<string> UpToDateSchemas => ImmutableStringHashSet.EmptyOrdinal
            .Add(CopyUpToDateMarker.SchemaName)
            .Add(UpToDateCheckInput.SchemaName)
            .Add(UpToDateCheckOutput.SchemaName)
            .Add(UpToDateCheckBuilt.SchemaName);

        private static ImmutableHashSet<string> ProjectPropertiesSchemas => ImmutableStringHashSet.EmptyOrdinal
            .Add(ConfigurationGeneral.SchemaName)
            .Union(ReferenceSchemas)
            .Union(UpToDateSchemas);

        private static ImmutableHashSet<string> NonCompilationItemTypes => ImmutableStringHashSet.EmptyOrdinal
            .Add(None.SchemaName)
            .Add(Content.SchemaName);

        private readonly IProjectSystemOptions _projectSystemOptions;
        private readonly ConfiguredProject _configuredProject;
        private readonly IProjectAsynchronousTasksService _tasksService;
        private readonly IProjectItemSchemaService _projectItemSchemaService;
        private readonly ITelemetryService _telemetryService;

        private IDisposable _link;
        private IComparable _lastVersionSeen;

        private bool _isDisabled = true;
        private bool _itemsChangedSinceLastCheck = true;
        private string _msBuildProjectFullPath;
        private string _msBuildProjectDirectory;
        private string _markerFile;
        private string _outputRelativeOrFullPath;

        private readonly HashSet<string> _imports = new HashSet<string>(StringComparers.Paths);
        private readonly HashSet<string> _itemTypes = new HashSet<string>(StringComparers.Paths);
        private readonly Dictionary<string, HashSet<(string Path, string Link, CopyToOutputDirectoryType CopyType)>> _items = new Dictionary<string, HashSet<(string, string, CopyToOutputDirectoryType)>>();
        private readonly HashSet<string> _customInputs = new HashSet<string>(StringComparers.Paths);
        private readonly HashSet<string> _customOutputs = new HashSet<string>(StringComparers.Paths);
        private readonly HashSet<string> _builtOutputs = new HashSet<string>(StringComparers.Paths);
        private readonly Dictionary<string, string> _copiedOutputFiles = new Dictionary<string, string>();
        private readonly HashSet<string> _analyzerReferences = new HashSet<string>(StringComparers.Paths);
        private readonly HashSet<string> _compilationReferences = new HashSet<string>(StringComparers.Paths);
        private readonly HashSet<string> _copyReferenceInputs = new HashSet<string>(StringComparers.Paths);

        [ImportingConstructor]
        public BuildUpToDateCheck(
            IProjectSystemOptions projectSystemOptions,
            ConfiguredProject configuredProject,
            [Import(ExportContractNames.Scopes.ConfiguredProject)] IProjectAsynchronousTasksService tasksService,
            IProjectItemSchemaService projectItemSchemaService,
            ITelemetryService telemetryService)
        {
            _projectSystemOptions = projectSystemOptions;
            _configuredProject = configuredProject;
            _tasksService = tasksService;
            _projectItemSchemaService = projectItemSchemaService;
            _telemetryService = telemetryService;
        }

        /// <summary>
        /// Called on project load.
        /// </summary>
        [ConfiguredProjectAutoLoad]
        [AppliesTo(ProjectCapability.CSharpOrVisualBasicOrFSharp + "+ !" + ProjectCapabilities.SharedAssetsProject)]
        internal void Load()
        {
            EnsureInitialized();
        }

        protected override void Initialize()
        {
            _link = ProjectDataSources.SyncLinkTo(
                _configuredProject.Services.ProjectSubscription.JointRuleSource.SourceBlock.SyncLinkOptions(new StandardRuleDataflowLinkOptions { RuleNames = ProjectPropertiesSchemas }),
                _configuredProject.Services.ProjectSubscription.ImportTreeSource.SourceBlock.SyncLinkOptions(),
                _configuredProject.Services.ProjectSubscription.SourceItemsRuleSource.SourceBlock.SyncLinkOptions(),
                _projectItemSchemaService.SourceBlock.SyncLinkOptions(),
                target: new ActionBlock<IProjectVersionedValue<Tuple<IProjectSubscriptionUpdate, IProjectImportTreeSnapshot, IProjectSubscriptionUpdate, IProjectItemSchema>>>(e => OnChanged(e)),
                linkOptions: new DataflowLinkOptions { PropagateCompletion = true });
        }

        private void OnProjectChanged(IProjectSubscriptionUpdate e)
        {
            _isDisabled = e.CurrentState.IsPropertyTrue(ConfigurationGeneral.SchemaName, ConfigurationGeneral.DisableFastUpToDateCheckProperty, defaultValue: false);

            _msBuildProjectFullPath = e.CurrentState.GetPropertyOrDefault(ConfigurationGeneral.SchemaName, ConfigurationGeneral.MSBuildProjectFullPathProperty, _msBuildProjectFullPath);
            _msBuildProjectDirectory = e.CurrentState.GetPropertyOrDefault(ConfigurationGeneral.SchemaName, ConfigurationGeneral.MSBuildProjectDirectoryProperty, _msBuildProjectDirectory);
            _outputRelativeOrFullPath = e.CurrentState.GetPropertyOrDefault(ConfigurationGeneral.SchemaName, ConfigurationGeneral.OutputPathProperty, _outputRelativeOrFullPath);

            if (e.ProjectChanges.TryGetValue(ResolvedAnalyzerReference.SchemaName, out var changes) &&
                changes.Difference.AnyChanges)
            {
                _analyzerReferences.Clear();
                _analyzerReferences.AddRange(changes.After.Items.Select(item => item.Value[ResolvedAnalyzerReference.ResolvedPathProperty]));
            }

            if (e.ProjectChanges.TryGetValue(ResolvedCompilationReference.SchemaName, out changes) &&
                changes.Difference.AnyChanges)
            {
                _compilationReferences.Clear();
                _copyReferenceInputs.Clear();

                foreach (var item in changes.After.Items)
                {
                    _compilationReferences.Add(item.Value[ResolvedCompilationReference.ResolvedPathProperty]);
                    if (!string.IsNullOrWhiteSpace(item.Value[CopyUpToDateMarker.SchemaName]))
                    {
                        _copyReferenceInputs.Add(item.Value[CopyUpToDateMarker.SchemaName]);
                    }
                    if (!string.IsNullOrWhiteSpace(item.Value[ResolvedCompilationReference.OriginalPathProperty]))
                    {
                        _copyReferenceInputs.Add(item.Value[ResolvedCompilationReference.OriginalPathProperty]);
                    }
                }
            }

            if (e.ProjectChanges.TryGetValue(UpToDateCheckInput.SchemaName, out var inputs) &&
                inputs.Difference.AnyChanges)
            {
                _customInputs.Clear();
                _customInputs.AddRange(inputs.After.Items.Select(item => _configuredProject.UnconfiguredProject.MakeRooted(item.Key)));
            }

            if (e.ProjectChanges.TryGetValue(UpToDateCheckOutput.SchemaName, out var outputs) &&
                outputs.Difference.AnyChanges)
            {
                _customOutputs.Clear();
                _customOutputs.AddRange(outputs.After.Items.Select(item => _configuredProject.UnconfiguredProject.MakeRooted(item.Key)));
            }

            if (e.ProjectChanges.TryGetValue(UpToDateCheckBuilt.SchemaName, out var built) &&
                built.Difference.AnyChanges)
            {
                _builtOutputs.Clear();

                foreach (var item in built.After.Items)
                {
                    var destination = item.Key;

                    if (item.Value.TryGetValue(UpToDateCheckBuilt.OriginalProperty, out var source) &&
                        !string.IsNullOrEmpty(source))
                    {
                        _copiedOutputFiles[destination] = source;
                    }
                    else
                    {
                        _builtOutputs.Add(destination);
                    }
                }
            }

            if (e.ProjectChanges.TryGetValue(CopyUpToDateMarker.SchemaName, out var upToDateMarkers) &&
                upToDateMarkers.Difference.AnyChanges)
            {
                _markerFile = upToDateMarkers.After.Items.Count == 1 ? _configuredProject.UnconfiguredProject.MakeRooted(upToDateMarkers.After.Items.Single().Key) : null;
            }
        }

        private void OnProjectImportsChanged(IProjectImportTreeSnapshot e)
        {
            void AddImports(IReadOnlyList<IProjectImportSnapshot> value)
            {
                foreach (var import in value)
                {
                    _imports.Add(import.ProjectPath);
                    AddImports(import.Imports);
                }
            }

            _imports.Clear();
            AddImports(e.Value);
        }

        private static string GetLink(IImmutableDictionary<string, string> itemMetadata) =>
            itemMetadata.TryGetValue(Link, out var link) ? link : null;

        private static CopyToOutputDirectoryType GetCopyType(IImmutableDictionary<string, string> itemMetadata)
        {
            if (itemMetadata.TryGetValue(CopyToOutputDirectory, out var value))
            {
                if (string.Equals(value, Always, StringComparison.OrdinalIgnoreCase))
                {
                    return CopyToOutputDirectoryType.CopyAlways;
                }

                if (string.Equals(value, PreserveNewest, StringComparison.OrdinalIgnoreCase))
                {
                    return CopyToOutputDirectoryType.CopyIfNewer;
                }
            }

            return CopyToOutputDirectoryType.CopyNever;
        }

        private void OnSourceItemChanged(IProjectSubscriptionUpdate e, IProjectItemSchema projectItemSchema)
        {
            var itemTypes = projectItemSchema.GetKnownItemTypes().Where(itemType => projectItemSchema.GetItemType(itemType).UpToDateCheckInput).ToArray();
            var itemTypesChanged = !_itemTypes.SetEquals(itemTypes);

            if (itemTypesChanged)
            {
                _itemTypes.Clear();
                _itemTypes.AddRange(itemTypes);
                _items.Clear();
            }

            foreach (var itemType in e.ProjectChanges.Where(changes => (itemTypesChanged || changes.Value.Difference.AnyChanges) && _itemTypes.Contains(changes.Key)))
            {
                var items = itemType.Value.After.Items
                    .Select(item => (_configuredProject.UnconfiguredProject.MakeRooted(item.Key), GetLink(item.Value), GetCopyType(item.Value)))
                    .Where(tuple => tuple.Item1 != null);
                _items[itemType.Key] = new HashSet<(string, string, CopyToOutputDirectoryType)>(items, UpToDateCheckItemComparer.Instance);
                _itemsChangedSinceLastCheck = true;
            }

            if (e.ProjectChanges.TryGetValue(UpToDateCheckOutput.SchemaName, out var outputs) &&
                outputs.Difference.AnyChanges)
            {
                _customOutputs.Clear();
                _customOutputs.AddRange(outputs.After.Items.Select(item => _configuredProject.UnconfiguredProject.MakeRooted(item.Key)));
            }
        }

        private void OnChanged(IProjectVersionedValue<Tuple<IProjectSubscriptionUpdate, IProjectImportTreeSnapshot, IProjectSubscriptionUpdate, IProjectItemSchema>> e)
        {
            OnProjectChanged(e.Value.Item1);
            OnProjectImportsChanged(e.Value.Item2);
            OnSourceItemChanged(e.Value.Item3, e.Value.Item4);
            _lastVersionSeen = e.DataSourceVersions[ProjectDataSources.ConfiguredProjectVersion];
        }

        protected override void Dispose(bool disposing)
        {
            _link?.Dispose();
        }

        private static DateTime? GetTimestamp(string path, IDictionary<string, DateTime> timestampCache)
        {
            if (!timestampCache.TryGetValue(path, out var time))
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return null;
                }
                time = info.LastWriteTimeUtc;
                timestampCache[path] = time;
            }

            return time;
        }

        private static void AddInput(BuildUpToDateCheckLogger logger, HashSet<string> inputs, string path)
        {
            logger.Verbose("    '{0}'", path);
            inputs.Add(path);
        }

        private static void AddInputs(BuildUpToDateCheckLogger logger, HashSet<string> inputs, IEnumerable<string> paths, string description)
        {
            var first = true;

            foreach (var path in paths)
            {
                if (first)
                {
                    logger.Verbose("Adding {0} inputs:", description);
                    first = false;
                }
                AddInput(logger, inputs, path);
            }
        }

        private static void AddOutput(BuildUpToDateCheckLogger logger, HashSet<string> outputs, string path)
        {
            logger.Verbose("    '{0}'", path);
            outputs.Add(path);
        }

        private static void AddOutputs(BuildUpToDateCheckLogger logger, HashSet<string> outputs, IEnumerable<string> paths, string description)
        {
            var first = true;

            foreach (var path in paths)
            {
                if (first)
                {
                    logger.Verbose("Adding {0} outputs:", description);
                    first = false;
                }
                AddOutput(logger, outputs, path);
            }
        }

        private bool Fail(BuildUpToDateCheckLogger logger, string message, string reason)
        {
            logger.Info(message);
            _telemetryService.PostProperty($"{TelemetryEventName}/Fail", "Reason", reason);
            return false;
        }

        private bool CheckGlobalConditions(BuildAction buildAction, BuildUpToDateCheckLogger logger)
        {
            if (buildAction != BuildAction.Build)
            {
                return false;
            }

            var itemsChangedSinceLastCheck = _itemsChangedSinceLastCheck;
            _itemsChangedSinceLastCheck = false;

            if (!_tasksService.IsTaskQueueEmpty(ProjectCriticalOperation.Build))
            {
                return Fail(logger, "Critical build tasks are running, not up to date.", "CriticalTasks");
            }

            if (_lastVersionSeen == null || _configuredProject.ProjectVersion.CompareTo(_lastVersionSeen) > 0)
            {
                return Fail(logger, "Project information is older than current project version, not up to date.", "ProjectInfoOutOfDate");
            }

            if (itemsChangedSinceLastCheck)
            {
                return Fail(logger, "The list of source items has changed since the last build, not up to date.", "ItemInfoOutOfDate");
            }

            if (_isDisabled)
            {
                return Fail(logger, "The 'DisableFastUpToDateCheckProperty' property is true, not up to date.", "Disabled");
            }

            var copyAlwaysItem = _items.SelectMany(kvp => kvp.Value).FirstOrDefault(item => item.CopyType == CopyToOutputDirectoryType.CopyAlways);
            if (copyAlwaysItem.Path != null)
            {
                logger.Info("Item '{0}' has CopyToOutputDirectory set to 'Always', not up to date.", copyAlwaysItem.Path);
            }

            return true;
        }

        private IEnumerable<string> CollectInputs(BuildUpToDateCheckLogger logger)
        {
            var inputs = new HashSet<string>(StringComparers.Paths);

            logger.Verbose("Adding project file inputs:");
            AddInput(logger, inputs, _msBuildProjectFullPath);

            AddInputs(logger, inputs, _imports, "import");

            foreach (var pair in _items.Where(kvp => !NonCompilationItemTypes.Contains(kvp.Key)))
            {
                AddInputs(logger, inputs, pair.Value.Select(item => item.Path), pair.Key);
            }

            AddInputs(logger, inputs, _analyzerReferences, ResolvedAnalyzerReference.SchemaName);
            AddInputs(logger, inputs, _compilationReferences, ResolvedCompilationReference.SchemaName);
            AddInputs(logger, inputs, _customInputs, UpToDateCheckInput.SchemaName);

            return inputs;
        }

        private IEnumerable<string> CollectOutputs(BuildUpToDateCheckLogger logger)
        {
            var outputs = new HashSet<string>(StringComparers.Paths);

            AddOutputs(logger, outputs, _customOutputs, UpToDateCheckOutput.SchemaName);
            AddOutputs(logger, outputs, _builtOutputs.Select(_configuredProject.UnconfiguredProject.MakeRooted), UpToDateCheckBuilt.SchemaName);

            return outputs;
        }

        private static (DateTime? time, string path) GetLatestInput(IEnumerable<string> inputs, IDictionary<string, DateTime> timestampCache, bool ignoreMissing = false)
        {
            DateTime? latest = DateTime.MinValue;
            string latestPath = null;

            foreach (var input in inputs)
            {
                var time = GetTimestamp(input, timestampCache);
                if (latest != null && (time == null && !ignoreMissing || time > latest))
                {
                    latest = time;
                    latestPath = input;
                }
            }

            return (latest, latestPath);
        }

        private static (DateTime? time, string path) GetEarliestOutput(IEnumerable<string> outputs, IDictionary<string, DateTime> timestampCache)
        {
            DateTime? earliest = DateTime.MaxValue;
            string earliestPath = null;

            foreach (var output in outputs)
            {
                var time = GetTimestamp(output, timestampCache);
                if (earliest != null && (time == null || time < earliest))
                {
                    earliest = time;
                    earliestPath = output;
                }
            }

            return (earliest, earliestPath);
        }

        // Reference assembly copy markers are strange. The property is always going to be present on 
        // references to SDK-based projects, regardless of whether or not those referenced projects 
        // will actually produce a marker. And an item always will be present in an SDK-based project, 
        // regardless of whether or not the project produces a marker. So, basically, we only check 
        // here if the project actually produced a marker and we only check it against references that
        // actually produced a marker.
        private bool CheckMarkers(BuildUpToDateCheckLogger logger, IDictionary<string, DateTime> timestampCache)
        {
            if (string.IsNullOrWhiteSpace(_markerFile) || !_copyReferenceInputs.Any())
            {
                return true;
            }

            logger.Verbose("Adding input reference copy markers:");

            foreach (var referenceMarkerFile in _copyReferenceInputs)
            {
                logger.Verbose("    '{0}'", referenceMarkerFile);
            }

            logger.Verbose("Adding output reference copy marker:");
            logger.Verbose("    '{0}'", _markerFile);

            (DateTime? inputMarkerTime, string inputMarkerPath) = GetLatestInput(_copyReferenceInputs, timestampCache, true);
            var outputMarkerTime = GetTimestamp(_markerFile, timestampCache);

            if (inputMarkerPath != null)
            {
                logger.Info("Latest write timestamp on input marker is {0} on '{1}'.", inputMarkerTime.Value, inputMarkerPath);
            }
            else
            {
                logger.Info("No input markers exist, skipping marker check.");
            }

            if (outputMarkerTime != null)
            {
                logger.Info("Write timestamp on output marker is {0} on '{1}'.", outputMarkerTime, _markerFile);
            }
            else
            {
                logger.Info("Output marker '{0}' does not exist, skipping marker check.", _markerFile);
            }

            if (outputMarkerTime <= inputMarkerTime)
            {
                logger.Info("Input marker is newer than output marker, not up to date.");
            }

            return inputMarkerPath == null || outputMarkerTime == null || outputMarkerTime > inputMarkerTime;
        }

        private bool CheckCopiedOutputFiles(BuildUpToDateCheckLogger logger, IDictionary<string, DateTime> timestampCache)
        {
            foreach (var kvp in _copiedOutputFiles)
            {
                var source = _configuredProject.UnconfiguredProject.MakeRooted(kvp.Value);
                var destination = _configuredProject.UnconfiguredProject.MakeRooted(kvp.Key);

                logger.Info("Checking build output file '{0}':", source);

                var itemTime = GetTimestamp(source, timestampCache);

                if (itemTime != null)
                {
                    logger.Info("    Source {0}: '{1}'.", itemTime, source);
                }
                else
                {
                    logger.Info("Source '{0}' does not exist, not up to date.", source);
                    return false;
                }

                var outputItemTime = GetTimestamp(destination, timestampCache);

                if (outputItemTime != null)
                {
                    logger.Info("    Destination {0}: '{1}'.", outputItemTime, destination);
                }
                else
                {
                    logger.Info("Destination '{0}' does not exist, not up to date.", destination);
                    return false;
                }

                if (outputItemTime < itemTime)
                {
                    logger.Info("Build output destination is newer than source, not up to date.");
                    return false;
                }
            }

            return true;
        }

        private bool CheckCopyToOutputDirectoryFiles(BuildUpToDateCheckLogger logger, IDictionary<string, DateTime> timestampCache)
        {
            var items = _items.SelectMany(kvp => kvp.Value).Where(item => item.CopyType == CopyToOutputDirectoryType.CopyIfNewer);

            string outputFullPath = Path.Combine(_msBuildProjectDirectory, _outputRelativeOrFullPath);

            foreach (var item in items)
            {
                var filename = string.IsNullOrEmpty(item.Link) ? item.Path : item.Link;

                if (string.IsNullOrEmpty(filename))
                {
                    continue;
                }

                filename = _configuredProject.UnconfiguredProject.MakeRelative(filename);

                logger.Info("Checking PreserveNewest file '{0}':", item.Path);

                var itemTime = GetTimestamp(item.Path, timestampCache);

                if (itemTime != null)
                {
                    logger.Info("    Source {0}: '{1}'.", itemTime, item.Path);
                }
                else
                {
                    logger.Info("Source '{0}' does not exist, not up to date.", item.Path);
                    return false;
                }

                var outputItem = Path.Combine(outputFullPath, filename);
                var outputItemTime = GetTimestamp(outputItem, timestampCache);

                if (outputItemTime != null)
                {
                    logger.Info("    Destination {0}: '{1}'.", outputItemTime, outputItem);
                }
                else
                {
                    logger.Info("Destination '{0}' does not exist, not up to date.", outputItem);
                    return false;
                }

                if (outputItemTime < itemTime)
                {
                    logger.Info("PreserveNewest destination is newer than source, not up to date.");
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> IsUpToDateAsync(BuildAction buildAction, TextWriter logWriter, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            EnsureInitialized();

            var requestedLogLevel = await _projectSystemOptions.GetFastUpToDateLoggingLevelAsync().ConfigureAwait(false);
            var logger = new BuildUpToDateCheckLogger(logWriter, requestedLogLevel, _configuredProject.UnconfiguredProject.FullPath);

            if (!CheckGlobalConditions(buildAction, logger))
            {
                return false;
            }

            var timestampCache = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            (DateTime? inputTime, string inputPath) = GetLatestInput(CollectInputs(logger), timestampCache);
            (DateTime? outputTime, string outputPath) = GetEarliestOutput(CollectOutputs(logger), timestampCache);

            if (inputTime != null)
            {
                logger.Info("Latest write timestamp on input is {0} on '{1}'.", inputTime.Value, inputPath);
            }
            else
            {
                logger.Info("Input '{0}' does not exist, not up to date.", inputPath);
            }

            if (outputTime != null)
            {
                logger.Info("Earliest write timestamp on output is {0} on '{1}'.", outputTime.Value, outputPath);
            }
            else
            {
                logger.Info("Output '{0}' does not exist, not up to date.", outputPath);
            }

            if (outputTime <= inputTime)
            {
                logger.Info("Output is newer than input, not up to date.");
            }

            // We are up to date if the earliest output write happened after the latest input write
            var markersUpToDate = CheckMarkers(logger, timestampCache);
            var outputsUpToDate = inputTime != null && outputTime != null && outputTime > inputTime;
            var copyToOutputDirectoryUpToDate = CheckCopyToOutputDirectoryFiles(logger, timestampCache);
            var copiedOutputUpToDate = CheckCopiedOutputFiles(logger, timestampCache);
            var isUpToDate = outputsUpToDate && markersUpToDate && copyToOutputDirectoryUpToDate && copiedOutputUpToDate;

            if (!markersUpToDate)
            {
                _telemetryService.PostProperty($"{TelemetryEventName}/Fail", "Reason", "Marker");
            }
            else if (!outputsUpToDate)
            {
                _telemetryService.PostProperty($"{TelemetryEventName}/Fail", "Reason", "Outputs");
            }
            else if (!copyToOutputDirectoryUpToDate)
            {
                _telemetryService.PostProperty($"{TelemetryEventName}/Fail", "Reason", "CopyToOutputDirectory");
            }
            else if (!copiedOutputUpToDate)
            {
                _telemetryService.PostProperty($"{TelemetryEventName}/Fail", "Reason", "CopyOutput");
            }
            else
            {
                _telemetryService.PostEvent($"{TelemetryEventName}/Success");
            }

            logger.Info("Project is{0} up to date.", !isUpToDate ? " not" : "");

            return isUpToDate;
        }

        public async Task<bool> IsUpToDateCheckEnabledAsync(CancellationToken cancellationToken = default(CancellationToken)) =>
            await _projectSystemOptions.GetIsFastUpToDateCheckEnabledAsync().ConfigureAwait(false);
    }
}
