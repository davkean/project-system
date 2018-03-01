// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.ProjectSystem.Build
{
    /// <summary>
    ///     Contains common MSBuild build properties.
    /// </summary>
    internal static class BuildProperty
    {
        /// <summary>
        ///     Indicates whether CoreCompile target should skip compiler execution completely.
        /// </summary>
        public static string SkipCompilerExecution = nameof(SkipCompilerExecution);

        /// <summary>
        ///     Indicates whether CoreCompile target should ouput the command-line 
        ///     that would have been passed to Csc.exe and Vbc.exe.
        /// </summary>
        public static string ProvideCommandLineArgs = nameof(ProvideCommandLineArgs);

        /// <summary>
        ///     Indicates whether Csc/Vbc tasks should call into the in-proc host compiler.
        /// </summary>
        public static string UseHostCompilerIfAvailable = nameof(UseHostCompilerIfAvailable);

        /// <summary>
        ///     Represents the "Application Designer" property pages to add 
        /// </summary>
        public static string PropertyPagesGuidsAdd = nameof(PropertyPagesGuidsAdd);

        public static string PropertyPagesGuidsRemove = nameof(PropertyPagesGuidsRemove);

        public static string CfgPropertyPagesGuidsAdd = nameof(CfgPropertyPagesGuidsAdd);

        public static string CfgPropertyPagesGuidsRemove = nameof(CfgPropertyPagesGuidsRemove);

        public static string LanguageSuffix = nameof(LanguageSuffix);
    }
}
