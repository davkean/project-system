﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Properties
{
    /// <summary>
    ///     Provides an implementation of <see cref="IProjectDesignerService"/> based on Visual Studio services.
    /// </summary>
    [Export(typeof(IProjectDesignerService))]
    internal class ProjectDesignerService : IProjectDesignerService
    {
        private readonly IUnconfiguredProjectVsServices _projectVsServices;
        private readonly bool _supportsProjectDesigner;

        [ImportingConstructor]
        public ProjectDesignerService(IUnconfiguredProjectVsServices projectVsServices)
        {
            Requires.NotNull(projectVsServices, nameof(projectVsServices));

            _projectVsServices = projectVsServices;
            _supportsProjectDesigner = _projectVsServices.VsHierarchy.GetProperty(VsHierarchyPropID.SupportsProjectDesigner, defaultValue: false);
        }

        public bool SupportsProjectDesigner => _supportsProjectDesigner;

        public Task ShowProjectDesignerAsync()
        {
            if (SupportsProjectDesigner)
            {
                return OpenProjectDesignerCoreAsync();
            }

            throw new InvalidOperationException("This project does not support the Project Designer (SupportsProjectDesigner is false).");
        }

        private async Task OpenProjectDesignerCoreAsync()
        {
            Guid projectDesignerGuid = _projectVsServices.VsHierarchy.GetGuidProperty(VsHierarchyPropID.ProjectDesignerEditor);

            IVsWindowFrame frame = _projectVsServices.VsProject.OpenItemWithSpecific(HierarchyId.Root, projectDesignerGuid);
            if (frame != null)
            {   // Opened within Visual Studio

                // Can only use Shell APIs on the UI thread
                await _projectVsServices.ThreadingService.SwitchToUIThread();

                HResult hr = frame.Show();
                if (hr.Failed)
                    throw hr.Exception;
            }
        }
    }
}
