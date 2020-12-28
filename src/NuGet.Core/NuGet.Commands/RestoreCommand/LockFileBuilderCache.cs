// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NuGet.ContentModel;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Repositories;
using NuGet.Shared;

namespace NuGet.Commands
{
    /// <summary>
    /// Cache objects used for building the lock file.
    /// </summary>
    public class LockFileBuilderCache
    {
        // Package files
        private readonly ConcurrentDictionary<PackageIdentity, ContentItemCollection> _contentItems
            = new ConcurrentDictionary<PackageIdentity, ContentItemCollection>();

        // OrderedCriteria is stored per target graph + override framework.
        private readonly ConcurrentDictionary<CriteriaKey, List<List<SelectionCriteria>>> _criteriaSets =
            new ConcurrentDictionary<CriteriaKey, List<List<SelectionCriteria>>>();

        private readonly ConcurrentDictionary<(CriteriaKey, LockFileLibrary), LockFileTargetLibrary> _lockFileTargetLibraryCache =
            new ConcurrentDictionary<(CriteriaKey, LockFileLibrary), LockFileTargetLibrary>();

        /// <summary>
        /// Get ordered selection criteria.
        /// </summary>
        public List<List<SelectionCriteria>> GetSelectionCriteria(RestoreTargetGraph graph, NuGetFramework framework)
        {
            // Criteria are unique on graph and framework override.
            var key = new CriteriaKey(graph.TargetGraphName, framework);

            if (!_criteriaSets.TryGetValue(key, out var criteria))
            {
                criteria = LockFileUtils.CreateOrderedCriteriaSets(graph, framework);
                _criteriaSets.TryAdd(key, criteria);
            }

            return criteria;
        }

        public LockFileTargetLibrary GetLockFileTargetLibrary(RestoreTargetGraph graph, NuGetFramework framework, LockFileLibrary lockFileLibrary)
        {
            // Criteria are unique on graph and framework override.
            var key = new CriteriaKey(graph.TargetGraphName, framework);
            if (_lockFileTargetLibraryCache.TryGetValue((key, lockFileLibrary), out var lockFileTargetLibrary))
            {
                return lockFileTargetLibrary;
            }

            return null;
        }

        public void SetLockFileTargetLibrary(RestoreTargetGraph graph, NuGetFramework framework, LockFileLibrary lockFileLibrary, LockFileTargetLibrary lockFileTargetLibrary)
        {
            // Criteria are unique on graph and framework override.
            var key = new CriteriaKey(graph.TargetGraphName, framework);
            _lockFileTargetLibraryCache.TryAdd((key, lockFileLibrary), lockFileTargetLibrary);
        }

        /// <summary>
        /// Get a ContentItemCollection of the package files.
        /// </summary>
        /// <remarks>Library is optional.</remarks>
        public ContentItemCollection GetContentItems(LockFileLibrary library, LocalPackageInfo package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var identity = new PackageIdentity(package.Id, package.Version);

            if (!_contentItems.TryGetValue(identity, out var collection))
            {
                collection = new ContentItemCollection();

                if (library == null)
                {
                    // Read folder
                    collection.Load(package.Files);
                }
                else
                {
                    // Use existing library
                    collection.Load(library.Files);
                }

                _contentItems.TryAdd(identity, collection);
            }

            return collection;
        }

        private class CriteriaKey : IEquatable<CriteriaKey>
        {
            public string TargetGraphName { get; }

            public NuGetFramework FrameworkOverride { get; }

            public CriteriaKey(string targetGraphName, NuGetFramework frameworkOverride)
            {
                TargetGraphName = targetGraphName;
                FrameworkOverride = frameworkOverride;
            }

            public bool Equals(CriteriaKey other)
            {
                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                if (ReferenceEquals(other, null))
                {
                    return false;
                }

                return StringComparer.Ordinal.Equals(TargetGraphName, other.TargetGraphName)
                    && FrameworkOverride.Equals(other.FrameworkOverride)
                    && other.FrameworkOverride.Equals(FrameworkOverride);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as CriteriaKey);
            }

            public override int GetHashCode()
            {
                var combiner = new HashCodeCombiner();

                combiner.AddObject(StringComparer.Ordinal.GetHashCode(TargetGraphName));
                combiner.AddObject(FrameworkOverride);

                return combiner.CombinedHash;
            }
        }
    }
}
