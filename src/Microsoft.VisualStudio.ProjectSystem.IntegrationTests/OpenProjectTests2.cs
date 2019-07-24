// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.TestTools.UnitTesting;

#nullable disable

namespace Microsoft.VisualStudio.ProjectSystem.VS
{
    [TestClass]
    public class CreateProjectTests2 : TestBase
    {
        [TestMethod]
        public void BuildAndRun_Fail()
        {
            _ = VisualStudio.ObjectModel.Solution;

            Assert.Fail();
        }

        protected override void PublishLogFiles()
        {
            base.PublishLogFiles();
        }
    }
}
