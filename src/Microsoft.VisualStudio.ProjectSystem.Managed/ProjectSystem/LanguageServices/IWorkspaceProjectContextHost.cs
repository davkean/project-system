// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.LanguageServices.ProjectSystem;

namespace Microsoft.VisualStudio.ProjectSystem.LanguageServices
{
    internal interface IWorkspaceProjectContextHost
    {
        Task Initialized
        {
            get;
        }

        Task OpenContextForRead(Func<IWorkspaceProjectContext, Task> action);
    }
}
