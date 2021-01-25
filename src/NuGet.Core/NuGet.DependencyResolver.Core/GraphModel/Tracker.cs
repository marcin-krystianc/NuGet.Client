// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

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
            foreach (var node in rootNode.EnumerateAllInTopologicalOrder())
            {
                Track(node);
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

                ascendants[outerNode] = 1;
            }

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

        public bool IsDowngraded(GraphNode<TItem> item)
        {
            var entry = GetEntry(item);

            var version = item.Item.Key.Version;

            foreach (var known in entry.Where(x => x.Disposition != Disposition.Rejected))
            {
                if (version > known.Item.Key.Version)
                {
                }
            }

            return false;
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
