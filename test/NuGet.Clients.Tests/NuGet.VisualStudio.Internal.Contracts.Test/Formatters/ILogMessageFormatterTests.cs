// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.Common;
using Xunit;

namespace NuGet.VisualStudio.Internal.Contracts.Test
{
    public sealed class ILogMessageFormatterTests : FormatterTests
    {
        [Theory]
        [MemberData(nameof(ILogMessages))]
        public void SerializeThenDeserialize_WithValidArguments_RoundTrips(ILogMessage expectedResult)
        {
            ILogMessage actualResult = SerializeThenDeserialize(ILogMessageFormatter.Instance, expectedResult);

            TestUtility.AssertEqual(expectedResult, actualResult);
        }

        public static TheoryData ILogMessages => new TheoryData<ILogMessage>
            {
                { new LogMessage(LogLevel.Error, message: "a", NuGetLogCode.NU3000) },
                {
                    new LogMessage(LogLevel.Warning, message: "a", NuGetLogCode.NU3027)
                    {
                        ProjectPath = "b",
                        WarningLevel = WarningLevel.Important
                    }
                }
            };
    }
}
