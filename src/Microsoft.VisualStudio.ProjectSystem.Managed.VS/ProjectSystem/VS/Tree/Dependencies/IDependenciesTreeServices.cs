﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;

using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.Snapshot;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies
{
    /// <summary>
    /// Abstracts tree nodes API and allows to use them outside of ProjectTreeProviderBase.
    /// </summary>
    [ProjectSystemContract(ProjectSystemContractScope.UnconfiguredProject, ProjectSystemContractProvider.System)]
    internal interface IDependenciesTreeServices
    {
        /// <summary>
        /// Creates IProjectItemTree - a tree node associated with a project item.
        /// </summary>
        /// <param name="caption"></param>
        /// <param name="itemContext"></param>
        /// <param name="propertySheet"></param>
        /// <param name="browseObjectProperties"></param>
        /// <param name="icon"></param>
        /// <param name="expandedIcon"></param>
        /// <param name="visible"></param>
        /// <param name="flags"></param>
        /// <returns></returns>
        IProjectTree CreateTree(
            string caption,
            IProjectPropertiesContext itemContext,
            IPropertySheet propertySheet = null,
            IRule browseObjectProperties = null,
            ProjectImageMoniker icon = null,
            ProjectImageMoniker expandedIcon = null,
            bool visible = true,
            ProjectTreeFlags? flags = default(ProjectTreeFlags?));

        /// <summary>
        /// Creates IProjectTree - a generic CPS tree node.
        /// </summary>
        /// <param name="caption"></param>
        /// <param name="filePath"></param>
        /// <param name="browseObjectProperties"></param>
        /// <param name="icon"></param>
        /// <param name="expandedIcon"></param>
        /// <param name="visible"></param>
        /// <param name="flags"></param>
        /// <returns></returns>
        IProjectTree CreateTree(
            string caption,
            string filePath,
            IRule browseObjectProperties = null,
            ProjectImageMoniker icon = null,
            ProjectImageMoniker expandedIcon = null,
            bool visible = true,
            ProjectTreeFlags? flags = default(ProjectTreeFlags?));

        /// <summary>
        /// Gets an IRule to attach to a project item, which would be used to 
        /// display browse object properties page.
        /// </summary>
        /// <param name="dependency"></param>
        /// <param name="catalogs"></param>
        /// <returns></returns>
        Task<IRule> GetRuleAsync(IDependency dependency, IProjectCatalogSnapshot catalogs);
    }
}
