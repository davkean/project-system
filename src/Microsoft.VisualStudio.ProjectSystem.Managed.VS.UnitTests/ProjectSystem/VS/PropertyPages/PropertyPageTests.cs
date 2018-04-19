﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;

using Microsoft.VisualStudio.OLE.Interop;

using Moq;
using Moq.Protected;

using Xunit;

namespace Microsoft.VisualStudio.ProjectSystem.VS.PropertyPages
{
    [Trait("UnitTest", "ProjectSystem")]
    public class PropertyPageTests
    {
        [Fact]
        public void GetPageInfoAndHelp()
        {
            Castle.DynamicProxy.Generators.AttributesToAvoidReplicating.Add(typeof(System.Security.Permissions.UIPermissionAttribute));

            var page = new Mock<PropertyPage>(false);
            page.Protected().Setup<string>("PropertyPageName").Returns("MyPage");
            var pageInfoArray = new PROPPAGEINFO[1];
            page.Object.GetPageInfo(pageInfoArray);
            page.Object.Help(string.Empty);

            PROPPAGEINFO info = pageInfoArray[0];
            Assert.Equal("MyPage", info.pszTitle);
            Assert.Equal(0u, info.dwHelpContext);
            Assert.Null(info.pszDocString);
            Assert.Null(info.pszHelpFile);
            Assert.Equal(page.Object.Size.Width, info.SIZE.cx);
            Assert.Equal(page.Object.Size.Height, info.SIZE.cy);
        }

        [Fact]
        public void MoveTest()
        {
            Castle.DynamicProxy.Generators.AttributesToAvoidReplicating.Add(typeof(System.Security.Permissions.UIPermissionAttribute));

            var rect = new RECT[] { new RECT() { left = 25, top = 25 } };
            var page = new Mock<PropertyPage>(false);
            page.CallBase = true;
            page.Object.Move(rect);

            Assert.Equal(rect[0].left, page.Object.Location.X);
            Assert.Equal(rect[0].top, page.Object.Location.Y);
        }

        [Fact]
        public void MoveThrowsArgumentExceptionIfNullRect()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Move(null);
            });
        }

        [Fact]
        public void MoveThrowsArgumentExceptionIfEmptyRect()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                Move(new RECT[0]);
            });
        }

        private static void Move(RECT[] x)
        {
            Castle.DynamicProxy.Generators.AttributesToAvoidReplicating.Add(typeof(System.Security.Permissions.UIPermissionAttribute));

            var page = new Mock<PropertyPage>(false);
            page.CallBase = true;

            page.Object.Move(x);
        }
    }
}
