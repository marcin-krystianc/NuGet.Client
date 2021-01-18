// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet.DependencyResolver
{
    internal class Tracker<TItem>
    {
        private readonly Dictionary<string, Entry> _entries
            = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public void Track(GraphNode<TItem> item)
        {
            var entry = GetEntry(item);
            if (!entry.List.Contains(item))
            {
                entry.List.Add(item);
            }
        }

        public bool IsBestVersion(GraphNode<TItem> item)
        {
            var entry = GetEntry(item);

            var version = item.Item.Key.Version;

            foreach (var known in entry.List.Where(x => x.Disposition != Disposition.Rejected))
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
            return entry.List.Any(x => x.Disposition == Disposition.Accepted);
        }

        internal void Clear()
        {
            _entries.Clear();
        }

        private Entry GetEntry(GraphNode<TItem> item)
        {
            Entry itemList;
            if (!_entries.TryGetValue(item.Key.Name, out itemList))
            {
                itemList = new Entry();
                _entries[item.Key.Name] = itemList;
            }
            return itemList;
        }

        private class Entry
        {
            public Entry()
            {
                List = new HashSet<GraphNode<TItem>>();
            }

            public HashSet<GraphNode<TItem>> List { get; set; }
        }
    }
}
