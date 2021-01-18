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

        public void Track(GraphNode<TItem> node)
        {
            var entry = GetEntry(node);
            entry.Add(node);
        }

        public void Remove(GraphNode<TItem> node)
        {
            var entry = GetEntry(node);
            entry.Remove(node);
        }

        public bool IsBestVersion(GraphNode<TItem> item)
        {
            var entry = GetEntry(item);

            var version = item.Item.Key.Version;

            foreach (var known in entry.Where(x => x.Disposition != Disposition.Rejected))
            {
                if (version < known.Item.Key.Version)
                {
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
