﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Microsoft.VisualStudio.ProjectSystem.Debug
{
    /// <summary>
    /// Interface definition for the LaunchSettingsProvider.
    /// </summary>
    [ProjectSystemContract(ProjectSystemContractScope.UnconfiguredProject, ProjectSystemContractProvider.System)]
    public interface ILaunchSettingsProvider
    {
        IReceivableSourceBlock<ILaunchSettings> SourceBlock { get; }

        ILaunchSettings CurrentSnapshot { get; }

        [Obsolete("Use ILaunchSettingsProvider2.GetLaunchSettingsFilePathAsync instead.")]
        string LaunchSettingsFile { get; }

        ILaunchProfile ActiveProfile { get; }

        // Replaces the current set of profiles with the contents of profiles. If changes were
        // made, the file will be checked out and updated. If the active profile is different, the
        // active profile property is updated.
        Task UpdateAndSaveSettingsAsync(ILaunchSettings profiles);

        // Blocks until at least one snapshot has been generated.
        Task<ILaunchSettings> WaitForFirstSnapshot(int timeout);

        /// <summary>
        /// Adds the given profile to the list and saves to disk. If a profile with the same 
        /// name exists (case sensitive), it will be replaced with the new profile. If addToFront is
        /// true the profile will be the first one in the list. This is useful since quite often callers want
        /// their just added profile to be listed first in the start menu. 
        /// </summary>
        Task AddOrUpdateProfileAsync(ILaunchProfile profile, bool addToFront);

        /// <summary>
        /// Removes the specified profile from the list and saves to disk.
        /// </summary>
        Task RemoveProfileAsync(string profileName);

        /// <summary>
        /// Adds or updates the global settings represented by settingName. Saves the 
        /// updated settings to disk. Note that the settings object must be serializable.
        /// </summary>
        Task AddOrUpdateGlobalSettingAsync(string settingName, object settingContent);

        /// <summary>
        /// Removes the specified global setting and saves the settings to disk
        /// </summary>
        Task RemoveGlobalSettingAsync(string settingName);

        /// <summary>
        /// Sets the active profile. This just sets the property it does not validate that the setting matches an
        /// existing profile
        /// </summary>
        Task SetActiveProfileAsync(string profileName);
    }
}

