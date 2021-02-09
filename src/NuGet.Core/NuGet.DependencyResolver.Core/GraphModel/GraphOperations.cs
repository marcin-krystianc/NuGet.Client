// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
//using CallContextProfiling;
using NuGet.LibraryModel;
using NuGet.Shared;
using NuGet.Versioning;

namespace NuGet.DependencyResolver
{
    public static class GraphOperations
    {
        private const string NodeArrow = " -> ";

        public static AnalyzeResult<RemoteResolveResult> Analyze(this GraphNode<RemoteResolveResult> root)
        {
            //using (CallContextProfiler.NamedStep("Analyze"))
            {
                var result = new AnalyzeResult<RemoteResolveResult>();

                root.TryResolveConflicts(result.VersionConflicts, result.Cycles, result.Downgrades);

                // Remove all downgrades that didn't result in selecting the node we actually downgraded to
                result.Downgrades.RemoveAll(d => d.DowngradedTo.Disposition != Disposition.Accepted);

                return result;
            }
        }

        private static void RemoveNode(GraphNode<RemoteResolveResult> node, Tracker<RemoteResolveResult> tracker)
        {
            var nodesToRemove = new Queue<GraphNode<RemoteResolveResult>>();
            nodesToRemove.Enqueue(node);

            while (nodesToRemove.Any())
            {
                var nodeToRemove = nodesToRemove.Dequeue();
                tracker.Remove(nodeToRemove);

                foreach (var outerNode in nodeToRemove.OuterNodes)
                {
                    outerNode.InnerNodes.Remove(nodeToRemove);
                }

                foreach (var innerNode in nodeToRemove.InnerNodes)
                {
                    innerNode.OuterNodes.Remove(nodeToRemove);
                    if (innerNode.OuterNodes.Count == 0)
                        nodesToRemove.Enqueue(innerNode);
                }
            }
        }

        private static bool IsEclipsed<TItem>(GraphNode<TItem> node)
        {
            //using (CallContextProfiler.NamedStep("IsEclipsed"))
            {
                var visitedNodes = new HashSet<GraphNode<TItem>>();
                var stack = new Stack<(GraphNode<TItem>, int)>();
                stack.Push((node, 0));

                while (stack.Count > 0)
                {
                    var (currentNode, idx) = stack.Pop();

                    while (true)
                    {
                        if (currentNode.OuterNodes.Count == 0)
                        {
                            return false;
                        }

                        if (idx >= currentNode.OuterNodes.Count)
                            break;

                        var outerNode = currentNode.OuterNodes[idx];
                        if (!visitedNodes.Add(outerNode))
                        {
                            idx++;
                            continue;
                        }

                        var sideNodes = outerNode.InnerNodes;
                        var count = sideNodes.Count;
                        var eclipsed = false;
                        for (var i = 0; i < count; i++)
                        {
                            var sideNode = sideNodes[i];

                            if (sideNode != node && node.Key.IsEclipsedBy(sideNode.Key))
                            {
                                eclipsed = true;
                                break;
                            }
                        }

                        if (eclipsed)
                        {
                            idx++;
                        }
                        else
                        {
                            stack.Push((currentNode, idx + 1));
                            currentNode = outerNode;
                            idx = 0;
                        }
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// A 1.0.0 -> B 1.0.0 -> C 2.0.0
        /// </summary>
        public static string GetPath<TItem>(this GraphNode<TItem> node)
        {
            var nodeStrings = new Stack<string>();
            var current = node;

            while (current != null)
            {
                nodeStrings.Push(current.GetIdAndVersionOrRange());
                current = current.OuterNodes.FirstOrDefault();
            }

            return string.Join(NodeArrow, nodeStrings);
        }

        /// <summary>
        /// A 1.0.0 -> B 1.0.0 -> C (= 2.0.0)
        /// </summary>
        public static string GetPathWithLastRange<TItem>(this GraphNode<TItem> node)
        {
            var nodeStrings = new Stack<string>();
            var current = node;

            while (current != null)
            {
                // Display the range for the last node, show the version of all parents.
                var nodeString = nodeStrings.Count == 0 ? current.GetIdAndRange() : current.GetIdAndVersionOrRange();
                nodeStrings.Push(nodeString);
                current = current.OuterNodes.FirstOrDefault();
            }

            return string.Join(NodeArrow, nodeStrings);
        }

        // A helper to navigate the graph nodes
        public static GraphNode<TItem> Path<TItem>(this GraphNode<TItem> node, params string[] path)
        {
            foreach (var item in path)
            {
                GraphNode<TItem> childNode = null;
                var innerNodes = node.InnerNodes;
                var count = innerNodes.Count;
                for (var i = 0; i < count; i++)
                {
                    var candidateNode = innerNodes[i];
                    if (StringComparer.OrdinalIgnoreCase.Equals(candidateNode.Key.Name, item))
                    {
                        childNode = candidateNode;
                        break;
                    }
                }

                if (childNode == null)
                {
                    return null;
                }

                node = childNode;
            }

            return node;
        }

        /// <summary>
        /// Prints the id and version constraint for a node.
        /// </summary>
        /// <remarks>Projects will not display a range.</remarks>
        public static string GetIdAndRange<TItem>(this GraphNode<TItem> node)
        {
            var id = node.GetId();

            // Ignore constraints for projects, they are not useful since
            // only one instance of the id may exist in the graph.
            if (node.IsPackage())
            {
                // Remove floating versions
                var range = node.GetVersionRange().ToNonSnapshotRange();

                // Print the version range if it has an upper or lower bound to display.
                if (range.HasLowerBound || range.HasUpperBound)
                {
                    return $"{id} {range.PrettyPrint()}";
                }
            }

            return id;
        }

        /// <summary>
        /// Prints the id and version of a node. If the version does not exist use the range.
        /// </summary>
        /// <remarks>Projects will not display a version or range.</remarks>
        public static string GetIdAndVersionOrRange<TItem>(this GraphNode<TItem> node)
        {
            var id = node.GetId();

            // Ignore versions for projects, they are not useful since
            // only one instance of the id may exist in the graph.
            if (node.IsPackage())
            {
                var version = node.GetVersionOrDefault();

                // Print the version if it exists, otherwise use the id.
                if (version != null)
                {
                    return $"{id} {version.ToNormalizedString()}";
                }
                else
                {
                    // The node was unresolved, use the range instead.
                    return node.GetIdAndRange();
                }
            }

            return id;
        }

        /// <summary>
        /// Id of the node.
        /// </summary>
        public static string GetId<TItem>(this GraphNode<TItem> node)
        {
            // Prefer the name of the resolved item, this will have
            // the correct casing. If it was not resolved use the parent
            // dependency for the name.
            return node.Item?.Key?.Name ?? node.Key.Name;
        }

        /// <summary>
        /// Version of the resolved node version if it exists.
        /// </summary>
        public static NuGetVersion GetVersionOrDefault<TItem>(this GraphNode<TItem> node)
        {
            // Prefer the name of the resolved item, this will have
            // the correct casing. If it was not resolved use the parent
            // dependency for the name.
            return node.Item?.Key?.Version;
        }

        /// <summary>
        /// Dependency range for the node.
        /// </summary>
        /// <remarks>Defaults to All</remarks>
        public static VersionRange GetVersionRange<TItem>(this GraphNode<TItem> node)
        {
            return node.Key.VersionRange ?? VersionRange.All;
        }

        /// <summary>
        /// True if the node is resolved to a package or allows a package if unresolved.
        /// </summary>
        public static bool IsPackage<TItem>(this GraphNode<TItem> node)
        {
            if ((node.Item?.Key?.Type == LibraryType.Package) == true)
            {
                // The resolved item is a package.
                return true;
            }

            // The node was unresolved but allows packages.
            return node.Key.TypeConstraintAllowsAnyOf(LibraryDependencyTarget.Package);
        }

        private static void TryResolveConflicts(this GraphNode<RemoteResolveResult> root, List<VersionConflictResult<RemoteResolveResult>> versionConflicts, List<GraphNode<RemoteResolveResult>> cycles, List<DowngradeResult<RemoteResolveResult>> downgrades)
        {
            // now we walk the tree as often as it takes to determine
            // which paths are accepted or rejected, based on conflicts occuring
            // between cousin packages

            var acceptedLibraries = Cache<RemoteResolveResult>.RentDictionary();
            var workingDowngrades = new Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>>();

            var patience = 1000;
            var incomplete = true;
;
            var tracker = Cache<RemoteResolveResult>.RentTracker();
            tracker.TrackRootNode(root);

            var centralTransitiveNodes = root.InnerNodes.Where(n => n.Item.IsCentralTransitive).ToList();
            var hasCentralTransitiveDependencies = centralTransitiveNodes.Count > 0;

            while (incomplete && --patience != 0)
            {
                if (patience == 1)
                {
                    throw new Exception("patience == 1");
                }

                if (hasCentralTransitiveDependencies)
                {
                    // Some of the central transitive nodes may be rejected now because their parents were rejected
                    // Reject them accordingly
                    root.RejectCentralTransitiveBecauseOfRejectedParents(tracker, centralTransitiveNodes);
                }

                foreach (var node in root.EnumerateAllInTopologicalOrder())
                {
                    WalkTreeAcceptOrRejectNodes(node, tracker, acceptedLibraries, workingDowngrades, cycles);
                }

                incomplete = root.EnumerateAll().Any(x => x.Disposition == Disposition.Acceptable);
            }

            foreach (var p in workingDowngrades)
            {
                downgrades.Add(new DowngradeResult<RemoteResolveResult>
                {
                    DowngradedFrom = p.Key,
                    DowngradedTo = p.Value
                });
            }

            foreach (var node in root.EnumerateAll())
            {
                DetectConflicts(node, versionConflicts, acceptedLibraries);
            }

            Cache<RemoteResolveResult>.ReleaseTracker(tracker);
            Cache<RemoteResolveResult>.ReleaseDictionary(acceptedLibraries);
        }

        private static void DetectConflicts<TItem>(GraphNode<TItem> node,  List<VersionConflictResult<TItem>> versionConflicts, Dictionary<string, GraphNode<TItem>> acceptedLibraries)
        {
            if (node.Disposition != Disposition.Rejected ||
                node.OuterNodes.All(x => x.Disposition != Disposition.Accepted))
                return;

            GraphNode<TItem> acceptedNode;
            if (acceptedLibraries.TryGetValue(node.Key.Name, out acceptedNode) &&
                node != acceptedNode &&
                node.Key.VersionRange != null &&
                acceptedNode.Item.Key.Version != null)
            {
                var acceptedType = LibraryDependencyTargetUtils.Parse(acceptedNode.Item.Key.Type);
                var type = node.Key.TypeConstraint;

                // Skip the check if a project reference override a package dependency
                // Check the type constraints, if there is any overlap check for conflict
                if ((acceptedType & (LibraryDependencyTarget.Project | LibraryDependencyTarget.ExternalProject)) ==
                    LibraryDependencyTarget.None
                    && (type & acceptedType) != LibraryDependencyTarget.None)
                {
                    var versionRange = node.Key.VersionRange;
                    var checkVersion = acceptedNode.Item.Key.Version;

                    if (!versionRange.Satisfies(checkVersion))
                    {
                        versionConflicts.Add(new VersionConflictResult<TItem>
                        {
                            Selected = acceptedNode, Conflicting = node
                        });
                    }
                }
            }
        }

        private static void WalkTreeAcceptOrRejectNodes(GraphNode<RemoteResolveResult> node,
            Tracker<RemoteResolveResult> tracker,
            Dictionary<string, GraphNode<RemoteResolveResult>> acceptedLibraries,
            Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>> workingDowngrades,
            List<GraphNode<RemoteResolveResult>> cycles)
        {
            if (node.Disposition == Disposition.Cycle)
            {
                cycles.Add(node);
                RemoveNode(node, tracker);
                return;
            }

            if (node.ParentNodes.Count > 0 && node.ParentNodes.All(x => x.Disposition != Disposition.Accepted))
            {
                return;
            }

            if (node.OuterNodes.Count > 0)
            {
                if (node.OuterNodes.All(x => x.Disposition == Disposition.Rejected))
                {
                    if (IsEclipsed(node))
                    {
                        RemoveNode(node, tracker);
                    }

                    node.Disposition = Disposition.Rejected;

                    return;
                }

                if (node.OuterNodes.Any(x => x.Disposition != Disposition.Accepted && x.Disposition != Disposition.Rejected))
                {
                    return;
                }
            }

            if (node.Disposition == Disposition.Acceptable)
            {
                var (eclipsed, by) = tracker.IsEclipsed2(node);
                if (eclipsed == 2)
                {
                    workingDowngrades.Add(node, by);
                    RemoveNode(node, tracker);
                    node.Disposition = Disposition.Rejected;
                }
                else if (eclipsed == 1)
                {
                    RemoveNode(node, tracker);
                }
                else
                {
                    if (tracker.IsBestVersion(node))
                    {
                        node.Disposition = Disposition.Accepted;
                        acceptedLibraries[node.Key.Name] = node;
                    }
                    else if (tracker.IsAnyVersionAccepted(node))
                    {
                        node.Disposition = Disposition.Rejected;
                        tracker.Untrack(node);
                    }
                }
            }
        }

        public static void ForEach<TItem>(this IEnumerable<GraphNode<TItem>> roots, Action<GraphNode<TItem>> visitor)
        {
            var queue = Cache<TItem>.RentQueue();

            var graphNodes = roots.AsList();
            var count = graphNodes.Count;
            for (var g = 0; g < count; g++)
            {
                queue.Enqueue(graphNodes[g]);
                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();

                    visitor(node);

                    AddInnerNodesToQueue(node.InnerNodes, queue);
                }
            }

            Cache<TItem>.ReleaseQueue(queue);
        }

        public static void ForEach<TItem>(this GraphNode<TItem> root, Action<GraphNode<TItem>> visitor)
        {
            var queue = Cache<TItem>.RentQueue();

            // breadth-first walk of Node tree, no state
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                visitor(node);
                AddInnerNodesToQueue(node.InnerNodes, queue);
            }

            Cache<TItem>.ReleaseQueue(queue);
        }

        public static IEnumerable<GraphNode<TItem>> EnumerateAll<TItem>(this GraphNode<TItem> root)
        {
            var queue = Cache<TItem>.RentQueue();
            var visitedNodes = new HashSet<GraphNode<TItem>>();

            // breadth-first walk of Node tree, visit each node only once
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!visitedNodes.Add(node))
                    continue;

                yield return node;
                foreach (var innerNode in node.InnerNodes)
                {
                    queue.Enqueue(innerNode);
                }
            }

            Cache<TItem>.ReleaseQueue(queue);
        }

        internal static IEnumerable<GraphNode<TItem>> EnumerateAllInTopologicalOrder<TItem>(this GraphNode<TItem> root)
        {
            var inDegree = root.EnumerateAll()
                .ToDictionary(x => x, x => x.OuterNodes.Count);

            var queue = Cache<TItem>.RentQueue();
            // breadth-first walk of Node tree, no state
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                yield return node;
                foreach (var innerNode in node.InnerNodes)
                {
                    if (--inDegree[innerNode] == 0)
                    {
                        queue.Enqueue(innerNode);
                    }
                }
            }
        }

        public static void ForEach<TItem, TContext>(this GraphNode<TItem> root, Action<GraphNode<TItem>, TContext> visitor, TContext context)
        {
            var queue = Cache<TItem>.RentQueue();
            // breadth-first walk of Node tree, no state
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();

                visitor(node, context);

                AddInnerNodesToQueue(node.InnerNodes, queue);
            }

            Cache<TItem>.ReleaseQueue(queue);
        }

        private static void AddInnerNodesToQueue<TItem>(IList<GraphNode<TItem>> innerNodes, Queue<GraphNode<TItem>> queue)
        {
            var count = innerNodes.Count;
            for (var i = 0; i < count; i++)
            {
                var innerNode = innerNodes[i];
                queue.Enqueue(innerNode);
            }
        }

        private static class Cache<TItem>
        {
            [ThreadStatic]
            private static Queue<GraphNode<TItem>> _queue;
            [ThreadStatic]
            private static Dictionary<string, GraphNode<TItem>> _dictionary;
            [ThreadStatic]
            private static Tracker<TItem> _tracker;

            public static Queue<GraphNode<TItem>> RentQueue()
            {
                var queue = _queue;
                if (queue != null)
                {
                    _queue = null;
                    return queue;
                }

                return new Queue<GraphNode<TItem>>();
            }

            public static void ReleaseQueue(Queue<GraphNode<TItem>> queue)
            {
                if (_queue == null)
                {
                    queue.Clear();
                    _queue = queue;
                }
            }

            public static Tracker<TItem> RentTracker()
            {
                var tracker = _tracker;
                if (tracker != null)
                {
                    _tracker = null;
                    return tracker;
                }

                return new Tracker<TItem>();
            }

            public static void ReleaseTracker(Tracker<TItem> tracker)
            {
                if (_tracker == null)
                {
                    tracker.Clear();
                    _tracker = tracker;
                }
            }

            public static Dictionary<string, GraphNode<TItem>> RentDictionary()
            {
                var dictionary = _dictionary;
                if (dictionary != null)
                {
                    _dictionary = null;
                    return dictionary;
                }

                return new Dictionary<string, GraphNode<TItem>>(StringComparer.OrdinalIgnoreCase);
            }

            public static void ReleaseDictionary(Dictionary<string, GraphNode<TItem>> dictionary)
            {
                if (_dictionary == null)
                {
                    dictionary.Clear();
                    _dictionary = dictionary;
                }
            }
        }

        private static void RejectCentralTransitiveBecauseOfRejectedParents<TItem>(this GraphNode<TItem> root, Tracker<TItem> tracker, List<GraphNode<TItem>> centralTransitiveNodes)
        {
            // If a node has its parents rejected, reject the node and its children
            // Need to do this in a loop because more nodes can be rejected as their parents become rejected
            bool pendingRejections = true;
            while (pendingRejections)
            {
                pendingRejections = false;
                for (int i = 0; i < centralTransitiveNodes.Count; i++)
                {
                    if (centralTransitiveNodes[i].Disposition == Disposition.Acceptable && centralTransitiveNodes[i].AreAllParentsRejected())
                    {
                        foreach (var node in centralTransitiveNodes[i].EnumerateAll())
                        {
                            node.Disposition = Disposition.Rejected;
                        }

                        pendingRejections = true;
                    }
                }
            }
        }

        // Box Drawing Unicode characters:
        // http://www.unicode.org/charts/PDF/U2500.pdf
        private const char LIGHT_HORIZONTAL = '\u2500';
        private const char LIGHT_VERTICAL_AND_RIGHT = '\u251C';

        [Conditional("DEBUG")]
        public static void Dump<TItem>(this GraphNode<TItem> root, Action<string> write)
        {
            DumpNode(root, write, level: 0);
            DumpChildren(root, write, level: 0);
        }

        private static void DumpChildren<TItem>(GraphNode<TItem> root, Action<string> write, int level)
        {
            var children = root.InnerNodes;
            for (var i = 0; i < children.Count; i++)
            {
                DumpNode(children[i], write, level + 1);
                DumpChildren(children[i], write, level + 1);
            }
        }

        private static void DumpNode<TItem>(GraphNode<TItem> node, Action<string> write, int level)
        {
            var output = new StringBuilder();
            if (level > 0)
            {
                output.Append(LIGHT_VERTICAL_AND_RIGHT);
                output.Append(new string(LIGHT_HORIZONTAL, level));
                output.Append(" ");
            }

            output.Append($"{node.GetIdAndRange()} ({node.Disposition})");

            if (node.Item != null
                && node.Item.Key != null)
            {
                output.Append($" => {node.Item.Key.ToString()}");
            }
            else
            {
                output.Append($" => ???");
            }
            write(output.ToString());
        }
    }
}
