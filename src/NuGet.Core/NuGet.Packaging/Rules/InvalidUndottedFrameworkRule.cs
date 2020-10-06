// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NuGet.Client;
using NuGet.Common;
using NuGet.ContentModel;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.RuntimeModel;

namespace NuGet.Packaging.Rules
{
    internal class InvalidUndottedFrameworkRule : IPackageRule
    {
        public string MessageFormat { get; }

        public InvalidUndottedFrameworkRule(string messageFormat)
        {
            MessageFormat = messageFormat;
        }

        public IEnumerable<PackagingLogMessage> Validate(PackageArchiveReader builder)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in builder.GetFiles().Select(t => PathUtility.GetPathWithDirectorySeparator(t)))
            {
                set.Add(file);
            }

            var managedCodeConventions = new ManagedCodeConventions(new RuntimeGraph());
            var collection = new ContentItemCollection();
            collection.Load(set.Select(path => path.Replace('\\', '/')).ToArray());

            var patterns = managedCodeConventions.Patterns;

            var frameworkPatterns = new List<PatternSet>()
            {
                patterns.RuntimeAssemblies,
                patterns.CompileRefAssemblies,
                patterns.CompileLibAssemblies,
                patterns.NativeLibraries,
                patterns.ResourceAssemblies,
                patterns.MSBuildFiles,
                patterns.ContentFiles,
                patterns.ToolsAssemblies,
                patterns.EmbedAssemblies,
                patterns.MSBuildTransitiveFiles
            };
            var warnPaths = new HashSet<string>();

            foreach (var pattern in frameworkPatterns)
            {
                IEnumerable<ContentItemGroup> targetedItemGroups = ContentExtractor.GetContentForPattern(collection, pattern);
                foreach (ContentItemGroup group in targetedItemGroups)
                {
                    foreach (ContentItem item in group.Items)
                    {
                        var frameworkString = (string)item.Properties["tfm_raw"];
                        var framework = (NuGetFramework)item.Properties["tfm"];
                        if (framework == null)
                        {
                            continue;
                        }

                        if (framework.Version.Major >= 5 &&
                            StringComparer.OrdinalIgnoreCase.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp, framework.Framework))
                        {
                            var dotIndex = frameworkString.IndexOf('.');
                            var dashIndex = frameworkString.IndexOf('-');
                            var frameworkVersionHasDots = (dashIndex > -1 && dotIndex > -1 && dotIndex < dashIndex) || (dashIndex == -1 && dotIndex > -1);
                            if (!frameworkVersionHasDots)
                            {
                                warnPaths.Add(item.Path);
                            }
                        }
                    }
                }
            }

            var output = new List<PackagingLogMessage>();

            if (warnPaths.Count > 0)
            {
                output.Add(CreatePackageIssue(string.Join(", ", warnPaths)));
            }

            return output;
        }

        private PackagingLogMessage CreatePackageIssue(string target)
        {
            return PackagingLogMessage.CreateWarning(
                string.Format(CultureInfo.CurrentCulture, MessageFormat, target),
                NuGetLogCode.NU5501);
        }
    }
}

