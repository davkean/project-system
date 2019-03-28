// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks.Dataflow;

using Microsoft.VisualStudio.ProjectSystem.Utilities;

using NuGet.SolutionRestoreManager;

using RestoreInfo = Microsoft.VisualStudio.ProjectSystem.IProjectVersionedValue<NuGet.SolutionRestoreManager.IVsProjectRestoreInfo>;

namespace Microsoft.VisualStudio.ProjectSystem.VS.NuGet
{
    [Export(typeof(IPackageRestoreUnconfiguredDataSource))]
    internal partial class PackageRestoreUnconfiguredDataSource : ChainedProjectValueDataSourceBase<IVsProjectRestoreInfo>, IPackageRestoreUnconfiguredDataSource
    {
        private readonly UnconfiguredProject _project;
        private readonly IActiveConfigurationGroupService _activeConfigurationGroupService;

        [ImportingConstructor]
        public PackageRestoreUnconfiguredDataSource(UnconfiguredProject project, IActiveConfigurationGroupService activeConfigurationGroupService)
            : base(project.Services, synchronousDisposal: true, registerDataSource: false)
        {
            _project = project;
            _activeConfigurationGroupService = activeConfigurationGroupService;
        }

        protected override UnconfiguredProject ContainingProject
        {
            get { return _project; }
        }

        protected override IDisposable LinkExternalInput(ITargetBlock<RestoreInfo> targetBlock)
        {
            // At a high-level, we want to combine all implicitly active configurations (ie the active config of each TFM) restore data
            // (via ProjectRestoreUpdate) and combine it into a single IVsProjectRestoreInfo instance and publish that. When a change is 
            // made to a configuration, such as adding a PackageReference, we should react to it and push a new version of our output. If the 
            // active configuration changes, we should react to it, and publish data from the new set of implicitly active configurations.
            var disposables = new DisposableBag();

            var packageRestoreConfiguredSource = new UnwrapCollectionChainedProjectValueDataSource<IReadOnlyCollection<ConfiguredProject>, ProjectRestoreUpdate>(
                _project.Services, c => c.Select(p => p.Services.ExportProvider.GetExportedValue<IPackageRestoreConfiguredDataSource>()),
                includeSourceVersions: false);  // TODO: Drop ConfiguredProjectIdentity/ConfiguredProjectVersion

            disposables.AddDisposable(packageRestoreConfiguredSource);

            IProjectValueDataSource<IConfigurationGroup<ConfiguredProject>> activeConfiguredProjectsSource = _activeConfigurationGroupService.ActiveConfiguredProjectGroupSource;
            disposables.AddDisposable(activeConfiguredProjectsSource.SourceBlock.LinkTo(packageRestoreConfiguredSource, DataflowOption.PropagateCompletion));

            // Transform all restore data -> combined restore data
            DisposableValue<ISourceBlock<RestoreInfo>> mergeBlock = packageRestoreConfiguredSource.SourceBlock
                                                                                                  .TransformWithNoDelta(update => update.Derive(MergeRestoreData));
            disposables.AddDisposable(mergeBlock);

            // Set the link up so that we publish changes to target block
            mergeBlock.Value.LinkTo(targetBlock, DataflowOption.PropagateCompletion);

            // Join the source blocks, so if they need to switch to UI thread to complete 
            // and someone is blocked on us on the same thread, the call proceeds
            JoinUpstreamDataSources(packageRestoreConfiguredSource, activeConfiguredProjectsSource);

            return disposables;
        }

        private IVsProjectRestoreInfo MergeRestoreData(IReadOnlyCollection<ProjectRestoreUpdate> updates)
        {
            // We need to combine the snapshots from each implicitly active configuration (ie per TFM), 
            // resolving any conflicts, which we'll report to the user. 

            string msbuildProjectExtensionsPath = ResolveMSBuildProjectExtensionsPathConflicts(updates);
            string originalTargetFrameworks = ResolveOriginalTargetFrameworksConflicts(updates);
            IVsReferenceItems toolReferences = ResolveToolReferenceConflicts(updates);
            IVsTargetFrameworks targetFrameworks = GetAllTargetFrameworks(updates);

            return new ProjectRestoreInfo(msbuildProjectExtensionsPath, 
                                          originalTargetFrameworks, 
                                          targetFrameworks, 
                                          toolReferences);
        }

        private string ResolveMSBuildProjectExtensionsPathConflicts(IEnumerable<ProjectRestoreUpdate> updates)
        {
            // All configurations need to agree on where the project-wide asset file is located.
            return ResolvePropertyConflicts(updates, u => u.BaseIntermediatePath, NuGetRestore.MSBuildProjectExtensionsPathProperty);
        }

        private string ResolveOriginalTargetFrameworksConflicts(IEnumerable<ProjectRestoreUpdate> updates)
        {
            // All configurations need to agree on what the overall "user-written" frameworks for the 
            // project so that conditions in the project-wide 'nuget.g.props' and 'nuget.g.targets' 
            // are written and evaluated correctly.
            return ResolvePropertyConflicts(updates, u => u.OriginalTargetFrameworks, NuGetRestore.TargetFrameworksProperty);
        }

        private string ResolvePropertyConflicts(IEnumerable<ProjectRestoreUpdate> updates, Func<IVsProjectRestoreInfo, string> propertyGetter, string propertyName)
        {
            // Always use the first TFM listed in project to provide consistent behavior
            ProjectRestoreUpdate update = updates.First();
            string propertyValue = propertyGetter(update.RestoreInfo);

            // Every config should had same value
            bool hasConflicts = updates.Select(u => propertyGetter(u.RestoreInfo))
                                       .Distinct(StringComparers.PropertyNames)
                                       .Count() > 1;

            if (hasConflicts)
            {
                ReportDataSourceUserFault(new Exception(string.Format(CultureInfo.CurrentCulture, VSResources.Restore_PropertyWithInconsistentValues, propertyName, propertyValue, update.ProjectConfiguration)),
                                          ProjectFaultSeverity.LimitedFunctionality,
                                          ContainingProject);
            }

            return propertyValue;
        }

        private IVsReferenceItems ResolveToolReferenceConflicts(IEnumerable<ProjectRestoreUpdate> updates)
        {
            var references = new Dictionary<string, IVsReferenceItem>(StringComparers.ItemNames);

            foreach (ProjectRestoreUpdate update in updates)
            {
                foreach (IVsReferenceItem reference in update.RestoreInfo.ToolReferences)
                {
                    if (references.TryGetValue(reference.Name, out IVsReferenceItem existingReference))
                    {
                        ReportUserFaultIfToolReferenceConflict(existingReference, reference);
                        continue;
                    }

                    references.Add(reference.Name, reference);
                }
            }

            return new ReferenceItems(references.Values);
        }

        private void ReportUserFaultIfToolReferenceConflict(IVsReferenceItem existingReference, IVsReferenceItem reference)
        {
            // CLI tool references are project-wide, so if they have conflicts in names, 
            // they must have the same metadata, which avoids from having to condition 
            // them so that they only appear in one TFM.
            if (!ReferenceItemEqualityComparer.Instance.Equals(existingReference, reference))
            {
                ReportDataSourceUserFault(new Exception(string.Format(CultureInfo.CurrentCulture, VSResources.Restore_DuplicateToolReferenceItems, existingReference.Name)),
                                          ProjectFaultSeverity.LimitedFunctionality,
                                          ContainingProject);
            }
        }

        private static IVsTargetFrameworks GetAllTargetFrameworks(IEnumerable<ProjectRestoreUpdate> updates)
        {
            IEnumerable<IVsTargetFrameworkInfo> targetFrameworks = updates.SelectMany(t => t.RestoreInfo.TargetFrameworks.Cast<IVsTargetFrameworkInfo>());

            return new TargetFrameworks(targetFrameworks);
        }

        //// We are taking source blocks from multiple configured projects and creating a SyncLink to combine the sources.
        //// The SyncLink will only publish data when the versions of the sources match. There is a problem with that.
        //// The sources have some version components that will make this impossible to match across TFMs. We introduce a 
        //// intermediate block here that will remove those version components so that the synclink can actually sync versions. 
        //IEnumerable<ProjectDataSources.SourceBlockAndLink<IProjectValueVersions>> sourceBlocks = e.Value.Select(
        //    cp =>
        //    {
        //        IReceivableSourceBlock<IProjectVersionedValue<IProjectSubscriptionUpdate>> sourceBlock = cp.Services.ProjectSubscription.JointRuleSource.SourceBlock;
        //        IPropagatorBlock<IProjectVersionedValue<IProjectSubscriptionUpdate>, IProjectVersionedValue<IProjectSubscriptionUpdate>> versionDropper = CreateVersionDropperBlock();
        //        disposableBag.AddDisposable(sourceBlock.LinkTo(versionDropper, sourceLinkOptions));
        //        return versionDropper.SyncLinkOptions<IProjectValueVersions>(sourceLinkOptions);
        //    });
    }
}
