// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Versioning;

//using CallContextProfiling;

namespace NuGet.DependencyResolver
{
    internal class Tracker<TItem>
    {
        private readonly Dictionary<string, HashSet<GraphNode<TItem>>> _entries
            = new Dictionary<string, HashSet<GraphNode<TItem>>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<GraphNode<TItem>, Dictionary<GraphNode<TItem>, long>> _ascendants =
            new Dictionary<GraphNode<TItem>, Dictionary<GraphNode<TItem>, long>>();

        public void TrackRootNode(GraphNode<TItem> rootNode)
        {
            _entries.Clear();
            if (_ascendants.Count > 0)
            {
            }

            _ascendants.Clear();

            //using (CallContextProfiler.NamedStep("TrackRootNode"))
            {
                foreach (var node in rootNode.EnumerateAllInTopologicalOrder())
                {
                    Track(node);
                }
            }
        }

        private void Track(GraphNode<TItem> node)
        {
            var ascendants = new Dictionary<GraphNode<TItem>, long>();
            _ascendants[node] = ascendants;

            foreach (var outerNode in node.OuterNodes)
            {
                foreach (var ascendant in _ascendants[outerNode])
                {
                    if (!ascendants.TryGetValue(ascendant.Key, out var current))
                    {
                        current = 0;
                    }

                    ascendants[ascendant.Key] = current + ascendant.Value;
                }
            }

            ascendants[node] = 1;

            var entry = GetEntry(node);
            entry.Add(node);
        }

        public void Untrack(GraphNode<TItem> node)
        {
            var nodeAscendants = _ascendants[node];

            foreach (var descendant in node.EnumerateAll())
            {
                if (descendant == node)
                    continue;

                var descendantAscendants = _ascendants[descendant];
                foreach (var kvp in nodeAscendants)
                {
                    descendantAscendants[kvp.Key] -= kvp.Value;
                }

                descendantAscendants[node] -= 1;
            }
        }

        public void Remove(GraphNode<TItem> node)
        {
            Untrack(node);
            var entry = GetEntry(node);
            entry.Remove(node);
        }

        public bool IsPotentiallyDowngraded(GraphNode<TItem> item)
        {
            //using (CallContextProfiler.NamedStep("IsPotentiallyDowngraded"))
            {
                var entry = GetEntry(item);

                var version = item.Item.Key.Version;

                foreach (var known in entry.Where(x => x.Disposition != Disposition.Rejected))
                {
                    if (version > known.Item.Key.Version)
                    {
                        // nearest-wins
                        if (_ascendants[item].Where(x => x.Value > 0).Select(x => x.Key).Intersect(known.OuterNodes)
                            .Any())
                            return true;
                    }
                }

                return false;
            }
        }


        public (int, GraphNode<TItem>)IsEclipsed2(GraphNode<TItem> nodeToCheck)
        {
            var root = _ascendants.Single(x => x.Value.Count == 1).Key;
            var paths = new Dictionary<GraphNode<TItem>, long>();

            var entry = GetEntry(nodeToCheck);
            var allNodes = entry
                .Where(x => x.Disposition != Disposition.Rejected)
                .Where(x => x != nodeToCheck)
                .SelectMany(x => x.OuterNodes)
                .ToList();

            if (nodeToCheck.Key.Name == "A" && nodeToCheck.Key.VersionRange.Equals(VersionRange.Parse("2.0")))
            {

            }

            var roots = _ascendants.ToDictionary(x => x.Key, x => x.Value[root]);

            foreach (var node in allNodes
                .Select(x => (x, allNodes.Intersect(_ascendants[x].Keys).Count()))
                .OrderBy(x => x.Item2)
                .Select(x => x.x))
            {
                foreach (var key in paths.Keys)
                {
                    if (!_ascendants[node].TryGetValue(key, out var tmp3))
                        continue;

                    roots[node] -= tmp3 * roots[key];
                }

                if (!_ascendants[nodeToCheck].TryGetValue(node, out var tmp2))
                {
                    tmp2 = 0;
                }

                var tmp1 = roots[node];
                var nPaths = tmp1 * tmp2;
                if (nPaths > 0)
                {
                    paths[node] = tmp1 * tmp2;
                }
            }

            if (!paths.Any() || paths.Sum(x => x.Value) < _ascendants[nodeToCheck][root])
            {
                return (0, null);
            }

            var versionsToCheck = entry
                .Where(x => x.Disposition != Disposition.Rejected)
                .Where(x => x != nodeToCheck)
                .Where(x => paths.Keys.Intersect(x.OuterNodes).Any())
                .ToList();

            if (versionsToCheck.All(x => x.Item.Key.Version != null && x.Item.Key.Version < nodeToCheck.Item.Key.Version))
            {
                // downgrade
                return (2, versionsToCheck.FirstOrDefault());
            }

            return (1, versionsToCheck.FirstOrDefault());
        }

        public bool IsBestVersion(GraphNode<TItem> item)
        {
            var entry = GetEntry(item);

            var version = item.Item.Key.Version;

            foreach (var known in entry.Where(x => x.Disposition != Disposition.Rejected))
            {
                if (version < known.Item.Key.Version)
                {
                    // nearest-wins
                    if (_ascendants[known].Where(x => x.Value > 0).Select(x => x.Key).Intersect(item.OuterNodes).Any())
                        continue;

                    return false;
                }
            }

            return true;
        }

        public bool IsAnyVersionAccepted(GraphNode<TItem> item)
        {
            var entry = GetEntry(item);
            return entry.Any(x => x.Disposition == Disposition.Accepted);
        }

        internal void Clear()
        {
            _entries.Clear();
        }

        private HashSet<GraphNode<TItem>> GetEntry(GraphNode<TItem> item)
        {
            if (!_entries.TryGetValue(item.Key.Name, out var itemList))
            {
                itemList = new HashSet<GraphNode<TItem>>();
                _entries[item.Key.Name] = itemList;
            }

            return itemList;
        }
    }
}
