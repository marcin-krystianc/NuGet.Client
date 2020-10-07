// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using NuGet.Common;
using Xunit;

namespace NuGet.VisualStudio.Internal.Contracts.Test
{
    internal static class TestUtility
    {
        internal static void AssertEqual(ILogMessage expectedResult, ILogMessage actualResult)
        {
            if (expectedResult is null)
            {
                Assert.Null(actualResult);

                return;
            }
            else
            {
                Assert.NotNull(actualResult);
            }

            Assert.Equal(expectedResult.Code, actualResult.Code);
            Assert.Equal(expectedResult.Level, actualResult.Level);
            Assert.Equal(expectedResult.Message, actualResult.Message);
            Assert.Equal(expectedResult.ProjectPath, actualResult.ProjectPath);
            Assert.Equal(expectedResult.Time, actualResult.Time);
            Assert.Equal(expectedResult.WarningLevel, actualResult.WarningLevel);
        }

        internal static void AssertEqual(
            IReadOnlyList<ILogMessage> expectedResults,
            IReadOnlyList<ILogMessage> actualResults)
        {
            if (expectedResults is null)
            {
                Assert.Null(actualResults);
            }
            else
            {
                Assert.NotNull(actualResults);
                Assert.Equal(expectedResults.Count, actualResults.Count);

                for (var i = 0; i < expectedResults.Count; ++i)
                {
                    TestUtility.AssertEqual(expectedResults[i], actualResults[i]);
                }
            }
        }
    }
}
