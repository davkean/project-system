﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;

using Microsoft.VisualStudio.ProjectSystem;

namespace Microsoft.VisualStudio.Telemetry
{
    [ProjectSystemContract(ProjectSystemContractScope.ProjectService, ProjectSystemContractProvider.System)]
    internal interface ITelemetryService
    {
        void PostEvent(string eventName);
        void PostProperty(string eventName, string propertyName, object propertyValue);
        void PostProperties(string eventName, IEnumerable<(string propertyName, object propertyValue)> properties);
        string HashValue(string value);
    }
}
