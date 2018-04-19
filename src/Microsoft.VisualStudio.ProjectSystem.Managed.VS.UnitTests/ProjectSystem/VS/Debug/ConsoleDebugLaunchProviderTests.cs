﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.IO;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.Utilities;

using Moq;

using Xunit;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Debug
{
    [Trait("UnitTest", "ProjectSystem")]
    public class ConsoleDebugLaunchProviderTest
    {
        private readonly string _ProjectFile = @"c:\test\project\project.csproj";
        private readonly string _Path = @"c:\program files\dotnet;c:\program files\SomeDirectory";
        private Mock<IEnvironmentHelper> _mockEnvironment = new Mock<IEnvironmentHelper>();
        private IFileSystemMock _mockFS = new IFileSystemMock();
        private Mock<IDebugTokenReplacer> _mockTokenReplace = new Mock<IDebugTokenReplacer>();

        private ConsoleDebugTargetsProvider GetDebugTargetsProvider(string outputType = "exe", Dictionary<string, string> properties = null)
        {
            _mockFS.WriteAllText(@"c:\test\Project\someapp.exe", "");
            _mockFS.CreateDirectory(@"c:\test\Project");
            _mockFS.CreateDirectory(@"c:\test\Project\bin\");
            _mockFS.WriteAllText(@"c:\program files\dotnet\dotnet.exe", "");

            var activeProfile = new LaunchProfile() { Name = "MyApplication", CommandLineArgs = "--someArgs", ExecutablePath = @"c:\test\Project\someapp.exe" };

            _mockEnvironment.Setup(s => s.GetEnvironmentVariable("Path")).Returns(() => _Path);

            var project = UnconfiguredProjectFactory.Create(null, null, _ProjectFile);

            var outputTypeEnum = new PageEnumValue(new EnumValue() { Name = outputType });
            var data = new PropertyPageData()
            {
                Category = ConfigurationGeneral.SchemaName,
                PropertyName = ConfigurationGeneral.OutputTypeProperty,
                Value = outputTypeEnum
            };
            var projectProperties = ProjectPropertiesFactory.Create(project, data);

            if (properties == null)
            {
                properties = new Dictionary<string, string>() {
                    {"RunCommand", @"dotnet"},
                    {"RunArguments", "exec " + "\"" + @"c:\test\project\bin\project.dll"+ "\""},
                    {"RunWorkingDirectory",  @"bin\"},
                    { "TargetFrameworkIdentifier", @".NetCoreApp" },
                    { "OutDir", @"c:\test\project\bin\" }
                };
            }
            var delegatePropertiesMock = IProjectPropertiesFactory
                .MockWithPropertiesAndValues(properties);

            var delegateProvider = IProjectPropertiesProviderFactory.Create(null, delegatePropertiesMock.Object);

            IConfiguredProjectServices configuredProjectServices = Mock.Of<IConfiguredProjectServices>(o =>
                o.ProjectPropertiesProvider == delegateProvider);

            ConfiguredProject configuredProject = Mock.Of<ConfiguredProject>(o =>
                o.UnconfiguredProject == project &&
                o.Services == configuredProjectServices);
            _mockTokenReplace.Setup(s => s.ReplaceTokensInProfileAsync(It.IsAny<ILaunchProfile>())).Returns<ILaunchProfile>(p => Task.FromResult(p));

            IActiveDebugFrameworkServices activeDebugFramework = Mock.Of<IActiveDebugFrameworkServices>(o =>
               o.GetConfiguredProjectForActiveFrameworkAsync() == Task.FromResult(configuredProject));
            var debugProvider = new ConsoleDebugTargetsProvider(
                                            configuredProject,
                                           _mockTokenReplace.Object,
                                           _mockFS,
                                           _mockEnvironment.Object,
                                           activeDebugFramework,
                                           projectProperties);
            return debugProvider;
        }

        [Fact]
        public void GetExeAndArguments()
        {
            var debugger = GetDebugTargetsProvider();

            string exeIn = @"c:\foo\bar.exe";
            string argsIn = "/foo /bar";
            string cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            debugger.GetExeAndArguments(false, exeIn, argsIn, out string finalExePath, out string finalArguments);
            Assert.Equal(finalExePath, exeIn);
            Assert.Equal(finalArguments, argsIn);

            debugger.GetExeAndArguments(true, exeIn, argsIn, out finalExePath, out finalArguments);
            Assert.Equal(cmdExePath, finalExePath);
            Assert.Equal("/c \"\"c:\\foo\\bar.exe\" /foo /bar & pause\"", finalArguments);
        }

        [Fact]
        public void GetExeAndArgumentsWithEscapedArgs()
        {
            var debugger = GetDebugTargetsProvider();

            string exeIn = @"c:\foo\bar.exe";
            string argsInWithEscapes = "/foo /bar ^ < > &";
            string cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            debugger.GetExeAndArguments(true, exeIn, argsInWithEscapes, out string finalExePath, out string finalArguments);
            Assert.Equal(cmdExePath, finalExePath);
            Assert.Equal("/c \"\"c:\\foo\\bar.exe\" /foo /bar ^^ ^< ^> ^& & pause\"", finalArguments);

            debugger.GetExeAndArguments(false, exeIn, argsInWithEscapes, out finalExePath, out finalArguments);
            Assert.Equal(exeIn, finalExePath);
            Assert.Equal(argsInWithEscapes, finalArguments);
        }

        [Fact]
        public void GetExeAndArgumentsWithNullArgs()
        {
            var debugger = GetDebugTargetsProvider();

            string exeIn = @"c:\foo\bar.exe";
            string cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            debugger.GetExeAndArguments(true, exeIn, null, out string finalExePath, out string finalArguments);
            Assert.Equal(cmdExePath, finalExePath);
            Assert.Equal("/c \"\"c:\\foo\\bar.exe\"  & pause\"", finalArguments);

        }

        [Fact]
        public void GetExeAndArgumentsWithEmptyArgs()
        {
            var debugger = GetDebugTargetsProvider();

            string exeIn = @"c:\foo\bar.exe";
            string cmdExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            // empty string args
            debugger.GetExeAndArguments(true, exeIn, null, out string finalExePath, out string finalArguments);
            Assert.Equal(cmdExePath, finalExePath);
            Assert.Equal("/c \"\"c:\\foo\\bar.exe\"  & pause\"", finalArguments);
        }

        [Fact]
        public async Task QueryDebugTargets_ProjectProfileAsyncF5()
        {
            var debugger = GetDebugTargetsProvider();

            _mockFS.WriteAllText(@"c:\program files\dotnet\dotnet.exe", "");
            _mockFS.CreateDirectory(@"c:\test\project");

            var activeProfile = new LaunchProfile() { Name = "MyApplication", CommandName = "Project", CommandLineArgs = "--someArgs" };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(@"c:\program files\dotnet\dotnet.exe", targets[0].Executable);
            Assert.Equal(DebugLaunchOperation.CreateProcess, targets[0].LaunchOperation);
            Assert.Equal(DebuggerEngines.ManagedCoreEngine, targets[0].LaunchDebugEngineGuid);
            Assert.Equal(0, targets[0].AdditionalDebugEngines.Count);
            Assert.Equal("exec \"c:\\test\\project\\bin\\project.dll\" --someArgs", targets[0].Arguments);
        }

        [Fact]
        public async Task QueryDebugTargets_ProjectProfileAsyncF5_NativeDebugging()
        {
            var debugger = GetDebugTargetsProvider();

            _mockFS.WriteAllText(@"c:\program files\dotnet\dotnet.exe", "");
            _mockFS.CreateDirectory(@"c:\test\project");

            var activeProfile = new LaunchProfile()
            {
                Name = "MyApplication",
                CommandName = "Project",
                CommandLineArgs = "--someArgs",
                OtherSettings = ImmutableStringDictionary<object>.EmptyOrdinal.Add(LaunchProfileExtensions.NativeDebuggingProperty, true)
            };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(@"c:\program files\dotnet\dotnet.exe", targets[0].Executable);
            Assert.Equal(DebugLaunchOperation.CreateProcess, targets[0].LaunchOperation);
            Assert.Equal(DebuggerEngines.ManagedCoreEngine, targets[0].LaunchDebugEngineGuid);
            Assert.Single(targets[0].AdditionalDebugEngines);
            Assert.Equal(DebuggerEngines.NativeOnlyEngine, targets[0].AdditionalDebugEngines[0]);
            Assert.Equal("exec \"c:\\test\\project\\bin\\project.dll\" --someArgs", targets[0].Arguments);
        }

        [Fact]
        public async Task QueryDebugTargets_ProjectProfileAsyncCtrlF5()
        {
            var debugger = GetDebugTargetsProvider();

            var activeProfile = new LaunchProfile() { Name = "MyApplication", CommandName = "Project", CommandLineArgs = "--someArgs" };

            // Now control-F5, add env
            activeProfile.EnvironmentVariables = new Dictionary<string, string>() { { "var1", "Value1" } }.ToImmutableDictionary();
            var targets = await debugger.QueryDebugTargetsAsync(DebugLaunchOptions.NoDebug, activeProfile);
            Assert.Single(targets);
            Assert.EndsWith(@"\cmd.exe", targets[0].Executable, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(DebugLaunchOperation.CreateProcess, targets[0].LaunchOperation);
            Assert.Equal((DebugLaunchOptions.NoDebug | DebugLaunchOptions.MergeEnvironment), targets[0].LaunchOptions);
            Assert.Equal(DebuggerEngines.ManagedCoreEngine, targets[0].LaunchDebugEngineGuid);
            Assert.True(targets[0].Environment.ContainsKey("var1"));
            Assert.Equal("/c \"\"c:\\program files\\dotnet\\dotnet.exe\" exec \"c:\\test\\project\\bin\\project.dll\" --someArgs & pause\"", targets[0].Arguments);
        }

        [Fact]
        public async Task QueryDebugTargets_ProjectProfileAsyncProfile()
        {
            var debugger = GetDebugTargetsProvider();

            var activeProfile = new LaunchProfile() { Name = "MyApplication", CommandName = "Project", CommandLineArgs = "--someArgs" };

            // Validate that when the DLO_Profiling is set we don't run the cmd.exe
            var targets = await debugger.QueryDebugTargetsAsync(DebugLaunchOptions.NoDebug | DebugLaunchOptions.Profiling, activeProfile);
            Assert.True(targets.Count == 1);
            Assert.Equal("c:\\program files\\dotnet\\dotnet.exe", targets[0].Executable);
            Assert.Equal((DebugLaunchOptions.NoDebug | DebugLaunchOptions.Profiling), targets[0].LaunchOptions);
        }

        [Fact]
        public async Task QueryDebugTargets_ExeProfileAsyncF5()
        {
            var debugger = GetDebugTargetsProvider();

            var activeProfile = new LaunchProfile() { Name = "MyApplication", CommandLineArgs = "--someArgs", ExecutablePath = @"c:\test\Project\someapp.exe" };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(activeProfile.ExecutablePath, targets[0].Executable);
            Assert.Equal(DebugLaunchOperation.CreateProcess, targets[0].LaunchOperation);
            Assert.Equal(DebuggerEngines.ManagedCoreEngine, targets[0].LaunchDebugEngineGuid);
            Assert.Equal("--someArgs", targets[0].Arguments);
        }

        [Fact]
        public async Task QueryDebugTargets_ExeProfileAsyncCtrlF5()
        {
            var debugger = GetDebugTargetsProvider();

            var activeProfile = new LaunchProfile() { Name = "MyApplication", CommandLineArgs = "--someArgs", ExecutablePath = @"c:\test\Project\someapp.exe" };

            // Now control-F5, add env vars
            activeProfile.EnvironmentVariables = new Dictionary<string, string>() { { "var1", "Value1" } }.ToImmutableDictionary();
            var targets = await debugger.QueryDebugTargetsAsync(DebugLaunchOptions.NoDebug, activeProfile);
            Assert.Single(targets);
            Assert.Equal(activeProfile.ExecutablePath, targets[0].Executable);
            Assert.Equal(DebugLaunchOperation.CreateProcess, targets[0].LaunchOperation);
            Assert.Equal((DebugLaunchOptions.NoDebug | DebugLaunchOptions.MergeEnvironment), targets[0].LaunchOptions);
            Assert.Equal(DebuggerEngines.ManagedCoreEngine, targets[0].LaunchDebugEngineGuid);
            Assert.Equal("--someArgs", targets[0].Arguments);
        }

        [Theory]
        [InlineData(@"c:\test\project\bin\")]
        [InlineData(@"bin\")]
        [InlineData(@"doesntExist\")]
        [InlineData(null)]
        public async Task QueryDebugTargets_ExeProfileAsyncExeRelativeNoWorkingDir(string outdir)
        {
            var properties = new Dictionary<string, string>() {
                    {"RunCommand", @"dotnet"},
                    {"RunArguments", "exec " + "\"" + @"c:\test\project\bin\project.dll"+ "\""},
                    {"RunWorkingDirectory",  @"bin\"},
                    { "TargetFrameworkIdentifier", @".NetCoreApp" },
                    { "OutDir", outdir }
                };

            var debugger = GetDebugTargetsProvider("exe", properties);

            // Exe relative, no working dir
            _mockFS.WriteAllText(@"c:\test\project\bin\test.exe", string.Empty);
            _mockFS.WriteAllText(@"c:\test\project\test.exe", string.Empty);
            var activeProfile = new LaunchProfile() { Name = "run", ExecutablePath = ".\\test.exe" };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            if (outdir == null || outdir == @"doesntExist\")
            {
                Assert.Equal(@"c:\test\project\test.exe", targets[0].Executable);
                Assert.Equal(@"c:\test\project", targets[0].CurrentDirectory);
            }
            else
            {
                Assert.Equal(@"c:\test\project\bin\test.exe", targets[0].Executable);
                Assert.Equal(@"c:\test\project\bin\", targets[0].CurrentDirectory);
            }
        }

        [Theory]
        [InlineData(@"c:\WorkingDir")]
        [InlineData(@"\WorkingDir")]
        public async Task QueryDebugTargets_ExeProfileAsyncExeRelativeToWorkingDir(string workingDir)
        {
            var debugger = GetDebugTargetsProvider();

            // Exe relative to full working dir
            _mockFS.WriteAllText(@"c:\WorkingDir\mytest.exe", string.Empty);
            _mockFS.SetCurrentDirectory(@"c:\Test");
            _mockFS.CreateDirectory(@"c:\WorkingDir");
            var activeProfile = new LaunchProfile() { Name = "run", ExecutablePath = ".\\mytest.exe", WorkingDirectory = workingDir };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(@"c:\WorkingDir\mytest.exe", targets[0].Executable);
            Assert.Equal(@"c:\WorkingDir", targets[0].CurrentDirectory);
        }

        [Fact]
        public async Task QueryDebugTargets_ExeProfileAsyncExeRelativeToWorkingDir_AlternateSlash()
        {
            var debugger = GetDebugTargetsProvider();

            // Exe relative to full working dir
            _mockFS.WriteAllText(@"c:\WorkingDir\mytest.exe", string.Empty);
            _mockFS.CreateDirectory(@"c:\WorkingDir");
            var activeProfile = new LaunchProfile() { Name = "run", ExecutablePath = "./mytest.exe", WorkingDirectory = @"c:/WorkingDir" };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(@"c:\WorkingDir\mytest.exe", targets[0].Executable);
            Assert.Equal(@"c:\WorkingDir", targets[0].CurrentDirectory);
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("dotnet.exe")]
        public async Task QueryDebugTargets_ExeProfileExeRelativeToPath(string exeName)
        {
            var debugger = GetDebugTargetsProvider();

            // Exe relative to path
            var activeProfile = new LaunchProfile() { Name = "run", ExecutablePath = exeName };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(@"c:\program files\dotnet\dotnet.exe", targets[0].Executable);
        }

        [Theory]
        [InlineData("myexe")]
        [InlineData("myexe.exe")]
        public async Task QueryDebugTargets_ExeProfileExeRelativeToCurrentDirectory(string exeName)
        {
            var debugger = GetDebugTargetsProvider();
            _mockFS.WriteAllText(@"c:\CurrentDirectory\myexe.exe", string.Empty);
            _mockFS.SetCurrentDirectory(@"c:\CurrentDirectory");

            // Exe relative to path
            var activeProfile = new LaunchProfile() { Name = "run", ExecutablePath = exeName };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(@"c:\CurrentDirectory\myexe.exe", targets[0].Executable);
        }

        [Fact]
        public async Task QueryDebugTargets_ExeProfileExeIsRootedWithNoDrive()
        {
            var debugger = GetDebugTargetsProvider();
            _mockFS.WriteAllText(@"e:\myexe.exe", string.Empty);
            _mockFS.SetCurrentDirectory(@"e:\CurrentDirectory");

            // Exe relative to path
            var activeProfile = new LaunchProfile() { Name = "run", ExecutablePath = @"\myexe.exe" };
            var targets = await debugger.QueryDebugTargetsAsync(0, activeProfile);
            Assert.Single(targets);
            Assert.Equal(@"e:\myexe.exe", targets[0].Executable);
        }

        [Fact]
        public void ValidateSettings_WhenNoExe_Throws()
        {
            string executable = null;
            string workingDir = null;
            var debugger = GetDebugTargetsProvider();
            var profileName = "run";

            Assert.Throws<Exception>(() =>
            {
                debugger.ValidateSettings(executable, workingDir, profileName);
            });

        }

        [Fact]
        public void ValidateSettings_WhenExeNotFoundThrows()
        {
            string executable = @"c:\foo\bar.exe";
            string workingDir = null;
            var debugger = GetDebugTargetsProvider();
            var profileName = "run";

            Assert.Throws<Exception>(() =>
            {
                debugger.ValidateSettings(executable, workingDir, profileName);
            });
        }

        [Fact]
        public void ValidateSettings_WhenExeFound_DoesNotThrow()
        {
            string executable = @"c:\foo\bar.exe";
            string workingDir = null;
            var debugger = GetDebugTargetsProvider();
            var profileName = "run";
            _mockFS.WriteAllText(executable, "");

            debugger.ValidateSettings(executable, workingDir, profileName);
            Assert.True(true);
        }

        [Fact]
        public void ValidateSettings_WhenWorkingDirNotFound_Throws()
        {
            string executable = "bar.exe";
            string workingDir = "c:\foo";
            var debugger = GetDebugTargetsProvider();
            var profileName = "run";

            Assert.Throws<Exception>(() =>
            {
                debugger.ValidateSettings(executable, workingDir, profileName);
            });
        }

        [Fact]
        public void ValidateSettings_WhenWorkingDirFound_DoesNotThrow()
        {
            string executable = "bar.exe";
            string workingDir = "c:\foo";
            var debugger = GetDebugTargetsProvider();
            var profileName = "run";

            _mockFS.AddFolder(workingDir);

            debugger.ValidateSettings(executable, workingDir, profileName);
            Assert.True(true);
        }

        [Theory]
        [InlineData("exec \"C:\\temp\\test.dll\"", "exec \"C:\\temp\\test.dll\"")]
        [InlineData("exec ^<>\"C:\\temp&^\\test.dll\"&", "exec ^^^<^>\"C:\\temp&^\\test.dll\"^&")]
        public void ConsoleDebugTargetsProvider_EscapeString_WorksCorrectly(string input, string expected)
        {
            Assert.Equal(expected, ConsoleDebugTargetsProvider.EscapeString(input, new[] { '^', '<', '>', '&' }));
        }
    }
}
