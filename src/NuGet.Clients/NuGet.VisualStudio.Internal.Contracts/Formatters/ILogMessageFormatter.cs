// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System;
using MessagePack;
using MessagePack.Formatters;
using Microsoft;
using NuGet.Common;

namespace NuGet.VisualStudio.Internal.Contracts
{
    internal sealed class ILogMessageFormatter : IMessagePackFormatter<ILogMessage?>
    {
        private const string CodePropertyName = "code";
        private const string LevelPropertyName = "level";
        private const string MessagePropertyName = "message";
        private const string ProjectPathPropertyName = "projectpath";
        private const string TimePropertyName = "time";
        private const string WarningLevelPropertyName = "warninglevel";

        internal static readonly IMessagePackFormatter<ILogMessage?> Instance = new ILogMessageFormatter();

        private ILogMessageFormatter()
        {
        }

        public ILogMessage? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return null;
            }

            // stack overflow mitigation - see https://github.com/neuecc/MessagePack-CSharp/security/advisories/GHSA-7q36-4xx7-xcxf
            options.Security.DepthStep(ref reader);

            try
            {
                NuGetLogCode? code = null;
                LogLevel? logLevel = null;
                string? message = null;
                string? projectPath = null;
                DateTimeOffset? time = null;
                WarningLevel? warningLevel = null;

                int propertyCount = reader.ReadMapHeader();

                for (var propertyIndex = 0; propertyIndex < propertyCount; ++propertyIndex)
                {
                    switch (reader.ReadString())
                    {
                        case CodePropertyName:
                            code = options.Resolver.GetFormatter<NuGetLogCode>().Deserialize(ref reader, options);
                            break;

                        case LevelPropertyName:
                            logLevel = options.Resolver.GetFormatter<LogLevel>().Deserialize(ref reader, options);
                            break;

                        case MessagePropertyName:
                            message = reader.ReadString();
                            break;

                        case ProjectPathPropertyName:
                            projectPath = reader.ReadString();
                            break;

                        case TimePropertyName:
                            time = options.Resolver.GetFormatter<DateTimeOffset>().Deserialize(ref reader, options);
                            break;

                        case WarningLevelPropertyName:
                            warningLevel = options.Resolver.GetFormatter<WarningLevel>().Deserialize(ref reader, options);
                            break;

                        default:
                            reader.Skip();
                            break;
                    }
                }

                Assumes.True(logLevel.HasValue);
                Assumes.NotNull(message);
                Assumes.True(time.HasValue);

                var logMessage = new LogMessage(logLevel.Value, message)
                {
                    ProjectPath = projectPath,
                    Time = time.Value
                };

                if (code.HasValue)
                {
                    logMessage.Code = code.Value;
                }

                if (warningLevel.HasValue)
                {
                    logMessage.WarningLevel = warningLevel.Value;
                }

                return logMessage;
            }
            finally
            {
                // stack overflow mitigation - see https://github.com/neuecc/MessagePack-CSharp/security/advisories/GHSA-7q36-4xx7-xcxf
                reader.Depth--;
            }
        }

        public void Serialize(ref MessagePackWriter writer, ILogMessage? value, MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            writer.WriteMapHeader(count: 6);
            writer.Write(CodePropertyName);
            options.Resolver.GetFormatter<NuGetLogCode>().Serialize(ref writer, value.Code, options);
            writer.Write(LevelPropertyName);
            options.Resolver.GetFormatter<LogLevel>().Serialize(ref writer, value.Level, options);
            writer.Write(MessagePropertyName);
            writer.Write(value.Message);
            writer.Write(ProjectPathPropertyName);
            writer.Write(value.ProjectPath);
            writer.Write(TimePropertyName);
            options.Resolver.GetFormatter<DateTimeOffset>().Serialize(ref writer, value.Time, options);
            writer.Write(WarningLevelPropertyName);
            options.Resolver.GetFormatter<WarningLevel>().Serialize(ref writer, value.WarningLevel, options);
        }
    }
}
