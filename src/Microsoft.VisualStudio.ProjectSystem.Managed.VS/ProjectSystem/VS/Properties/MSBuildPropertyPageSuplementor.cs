//// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

//using System;
//using System.Collections.Generic;
//using System.Collections.Immutable;
//using System.ComponentModel.Composition;
//using System.Linq;
//using System.Threading.Tasks;
//using Microsoft.Build.Evaluation;
//using Microsoft.VisualStudio.ProjectSystem.Build;

//namespace Microsoft.VisualStudio.ProjectSystem.VS.Properties
//{
//    [Export(typeof(IVsPropertyPagesCatalog))]
//    internal class MSBuildPropertyPageSuplementor : IVsPropertyPagesCatalog
//    {
//        private readonly IProjectAccessor _accessor;
//        private readonly ActiveConfiguredProject<ConfiguredProject> _activeConfiguredProject;

//        [ImportingConstructor]
//        public MSBuildPropertyPageSuplementor(IProjectAccessor accessor, ActiveConfiguredProject<ConfiguredProject> activeConfiguredProject)
//        {
//            _accessor = accessor;
//            _activeConfiguredProject = activeConfiguredProject;
//        }

//        public async Task<IReadOnlyCollection<IPageMetadata>> AddToAsync(IReadOnlyCollection<IPageMetadata> pages)
//        {
//            Requires.NotNull(pages, nameof(pages));

//            var actions = await GetPageActionsAsync().ConfigureAwait(false);

//            ImmutableList<IPageMetadata>.Builder result = ImmutableList.CreateBuilder<IPageMetadata>();
//            foreach (IPageMetadata page in pages)
//            {
//                var pagesToRemove = page.HasConfigurationCondition ? actions.configurationPagesToRemove : actions.pagesToRemove;

//                // NOTE: To maintain compat with legacy project system, removes only affect built-in pages, never the "additional" pages
//                if (!pagesToRemove.Contains(page.PageGuid))
//                {
//                    result.Add(page);
//                }
//            }
//        }

//        private Task<(IEnumerable<Guid> pagesToAdd, IEnumerable<Guid> pagesToRemove, IEnumerable<Guid> configurationPagesToAdd, IEnumerable<Guid> configurationPagesToRemove)> GetPageActionsAsync()
//        {

//        }

//        private Task<(string pagesToAdd, string pagesToRemove, string configurationPagesToAdd, string configurationPagesToRemove)> GetPageActionsAsUnparsedStringAsync()
//        {
//            return _accessor.OpenProjectForReadAsync(_activeConfiguredProject.Value, project =>
//            {
//                string languageSuffix = GetEvaluatedPropertyValue(project, BuildProperty.LanguageSuffix);
//                string pagesToAdd = GetSemiColonDelimitedList(project, BuildProperty.PropertyPagesGuidsAdd, languageSuffix);
//                string pagesToRemove = GetSemiColonDelimitedList(project, BuildProperty.PropertyPagesGuidsRemove, languageSuffix);
//                string configurationPagesToAdd = GetSemiColonDelimitedList(project, BuildProperty.CfgPropertyPagesGuidsAdd, languageSuffix);
//                string configurationPagesToRemove = GetSemiColonDelimitedList(project, BuildProperty.CfgPropertyPagesGuidsRemove, languageSuffix);

//                return (pagesToAdd, pagesToRemove, configurationPagesToAdd, configurationPagesToRemove);

//            });
//        }

//        private string GetSemiColonDelimitedList(Project project, string propertyName, string languageSuffix)
//        {
//            string pages = GetEvaluatedPropertyValue(project, propertyName);
//            if (languageSuffix != null)
//            {   // Any language specific pages?

//                pages += ";" + GetEvaluatedPropertyValue(project, propertyName + languageSuffix);
//            }

//            return pages;
//        }

//        private string GetEvaluatedPropertyValue(Project project, string name)
//        {
//            ProjectProperty property = project.GetProperty(name);
//            if (property == null)
//                return string.Empty;

//            return property.EvaluatedValue;
//        }
//    }
//}
