﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;

using Moq;

namespace Microsoft.VisualStudio.Shell.Interop
{
    internal static class IVsUpgradeLoggerFactory
    {
        public static IVsUpgradeLogger CreateLogger(IList<LogMessage> messages)
        {
            var mock = new Mock<IVsUpgradeLogger>();

            mock.Setup(pl => pl.LogMessage(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<uint, string, string, string>((level, project, file, message) => messages.Add(new LogMessage
                {
                    Level = level,
                    File = file,
                    Project = project,
                    Message = message
                }));

            return mock.Object;
        }
    }

    internal class LogMessage
    {
        public uint Level { get; set; }
        public string Project { get; set; }
        public string File { get; set; }
        public string Message { get; set; }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (!(obj is LogMessage other))
            {
                return false;
            }

            return Level == other.Level && Project.Equals(other.Project) && File.Equals(other.File) && Message.Equals(other.Message);
        }

        public override int GetHashCode()
        {
            return Level.GetHashCode() * 31 + Project.GetHashCode() * 3 + File.GetHashCode() * 7 + Message.GetHashCode() * 5;
        }
    }
}
