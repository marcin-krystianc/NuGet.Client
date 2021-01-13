// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
            var result = new AnalyzeResult<RemoteResolveResult>();

            root.CheckCycleAndNearestWins(result.Downgrades, result.Cycles);
            root.TryResolveConflicts(result.VersionConflicts);

            // Remove all downgrades that didn't result in selecting the node we actually downgraded to
            result.Downgrades.RemoveAll(d => d.DowngradedTo.Disposition != Disposition.Accepted);

            if (root.EnumerateAll().FirstOrDefault(x => x.Disposition == Disposition.Acceptable) != null)
                throw new Exception();

            return result;
        }

        // Verifies if minimum version specification for nearVersion is greater than the
        // minimum version specification for farVersion
        public static bool IsGreaterThanOrEqualTo(VersionRange nearVersion, VersionRange farVersion)
        {
            if (!nearVersion.HasLowerBound)
            {
                return true;
            }
            else if (!farVersion.HasLowerBound)
            {
                return false;
            }
            else if (nearVersion.IsFloating || farVersion.IsFloating)
            {
                NuGetVersion nearMinVersion;
                NuGetVersion farMinVersion;

                string nearRelease;
                string farRelease;

                if (nearVersion.IsFloating)
                {
                    if (nearVersion.Float.FloatBehavior == NuGetVersionFloatBehavior.Major)
                    {
                        // nearVersion: "*"
                        return true;
                    }

                    nearMinVersion = GetReleaseLabelFreeVersion(nearVersion);
                    nearRelease = nearVersion.Float.OriginalReleasePrefix;
                }
                else
                {
                    nearMinVersion = nearVersion.MinVersion;
                    nearRelease = nearVersion.MinVersion.Release;
                }

                if (farVersion.IsFloating)
                {
                    if (farVersion.Float.FloatBehavior == NuGetVersionFloatBehavior.Major)
                    {
                        // farVersion: "*"
                        return false;
                    }

                    farMinVersion = GetReleaseLabelFreeVersion(farVersion);
                    farRelease = farVersion.Float.OriginalReleasePrefix;
                }
                else
                {
                    farMinVersion = farVersion.MinVersion;
                    farRelease = farVersion.MinVersion.Release;
                }

                var result = nearMinVersion.CompareTo(farMinVersion, VersionComparison.Version);
                if (result != 0)
                {
                    return result > 0;
                }

                if (string.IsNullOrEmpty(nearRelease))
                {
                    // near is 1.0.0-*
                    return true;
                }
                else if (string.IsNullOrEmpty(farRelease))
                {
                    // near is 1.0.0-alpha-* and far is 1.0.0-*
                    return false;
                }
                else
                {
                    var lengthToCompare = Math.Min(nearRelease.Length, farRelease.Length);

                    return StringComparer.OrdinalIgnoreCase.Compare(
                        nearRelease.Substring(0, lengthToCompare),
                        farRelease.Substring(0, lengthToCompare)) >= 0;
                }
            }

            return nearVersion.MinVersion >= farVersion.MinVersion;
        }

        private static NuGetVersion GetReleaseLabelFreeVersion(VersionRange versionRange)
        {
            if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Major)
            {
                return new NuGetVersion(int.MaxValue, int.MaxValue, int.MaxValue);
            }
            else if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Minor)
            {
                return new NuGetVersion(versionRange.MinVersion.Major, int.MaxValue, int.MaxValue, int.MaxValue);
            }
            else if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Patch)
            {
                return new NuGetVersion(versionRange.MinVersion.Major, versionRange.MinVersion.Minor, int.MaxValue, int.MaxValue);
            }
            else if (versionRange.Float.FloatBehavior == NuGetVersionFloatBehavior.Revision)
            {
                return new NuGetVersion(
                    versionRange.MinVersion.Major,
                    versionRange.MinVersion.Minor,
                    versionRange.MinVersion.Patch,
                    int.MaxValue);
            }
            else
            {
                return new NuGetVersion(
                    versionRange.MinVersion.Major,
                    versionRange.MinVersion.Minor,
                    versionRange.MinVersion.Patch,
                    versionRange.MinVersion.Revision);
            }
        }

        private static void CheckCycleAndNearestWins(
            this GraphNode<RemoteResolveResult> root,
            List<DowngradeResult<RemoteResolveResult>> downgrades,
            List<GraphNode<RemoteResolveResult>> cycles)
        {
            var workingDowngrades = RentDowngradesDictionary();

            foreach (var node in root.EnumerateAll())
            {
                WalkTreeCheckCycleAndNearestWins(CreateState(cycles, workingDowngrades), node);
            }

#if IS_DESKTOP || NETSTANDARD2_0
            // Increase List size for items to be added, if too small
            var requiredCapacity = downgrades.Count + workingDowngrades.Count;
            if (downgrades.Capacity < requiredCapacity)
            {
                downgrades.Capacity = requiredCapacity;
            }
#endif
            foreach (var p in workingDowngrades)
            {
                downgrades.Add(new DowngradeResult<RemoteResolveResult>
                {
                    DowngradedFrom = p.Key,
                    DowngradedTo = p.Value
                });
            }

            ReleaseDowngradesDictionary(workingDowngrades);
        }

        private static void WalkTreeCheckCycle(List<GraphNode<RemoteResolveResult>> cycles,
            GraphNode<RemoteResolveResult> node)
        {
            if (node.Disposition != Disposition.Cycle)
                return;

            cycles.Add(node);
        }

        private static void WalkTreeCheckCycleAndNearestWins(CyclesAndDowngrades context, GraphNode<RemoteResolveResult> node)
        {
            // Cycle:
            //
            // A -> B -> A (cycle)
            //
            // Downgrade:
            //
            // A -> B -> C -> D 2.0 (downgrade)
            //        -> D 1.0
            //
            // Potential downgrades that turns out to not be downgrades:
            //
            // 1. This should never happen in practice since B would have never been valid to begin with.
            //
            //    A -> B -> C -> D 2.0
            //           -> D 1.0
            //      -> D 2.0
            //
            // 2. This occurs if none of the sources have version C 1.0 so C 1.0 is bumped up to C 2.0.
            //
            //   A -> B -> C 2.0
            //     -> C 1.0

            var cycles = context.Cycles;
            var workingDowngrades = context.Downgrades;

            if (node.Disposition == Disposition.Cycle)
            {
                cycles.Add(node);

                // Remove this node from the tree so the nothing else evaluates this.
                // This is ok since we have a parent pointer and we can still print the path
                foreach (var outerNode in node.OuterNodes)
                {
                    outerNode.InnerNodes.Remove(node);
                }

                return;
            }

            if (node.Disposition != Disposition.PotentiallyDowngraded)
            {
                return;
            }



            // REVIEW: This could probably be done in a single pass where we keep track
            // of what is nearer as we walk down the graph (BFS)
            for (var n = node.OuterNodes.FirstOrDefault(); n != null; n = n.OuterNodes.FirstOrDefault())
            {
                var innerNodes = n.InnerNodes;
                var count = innerNodes.Count;
                for (var i = 0; i < count; i++)
                {
                    var sideNode = innerNodes[i];
                    if (sideNode != node && StringComparer.OrdinalIgnoreCase.Equals(sideNode.Key.Name, node.Key.Name))
                    {
                        // Nodes that have no version range should be ignored as potential downgrades e.g. framework reference
                        if (sideNode.Key.VersionRange != null &&
                            node.Key.VersionRange != null &&
                            !RemoteDependencyWalker.IsGreaterThanOrEqualTo(sideNode.Key.VersionRange, node.Key.VersionRange))
                        {
                            // Is the resolved version actually within node's version range? This happen if there
                            // was a different request for a lower version of the library than this version range
                            // allows but no matching library was found, so the library is bumped up into this
                            // version range.
                            var resolvedVersion = sideNode?.Item?.Data?.Match?.Library?.Version;
                            if (resolvedVersion != null && node.Key.VersionRange.Satisfies(resolvedVersion))
                            {
                                continue;
                            }

                            workingDowngrades[node] = sideNode;
                        }
                        else
                        {
                            workingDowngrades.Remove(node);
                        }
                    }
                }
            }

            // Remove this node from the tree so the nothing else evaluates this.
            // This is ok since we have a parent pointer and we can still print the path
            foreach (var outerNode in node.OuterNodes)
            {
                outerNode.InnerNodes.Remove(node);
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

        private static bool TryResolveConflicts<TItem>(this GraphNode<TItem> root, List<VersionConflictResult<TItem>> versionConflicts)
        {
            // now we walk the tree as often as it takes to determine
            // which paths are accepted or rejected, based on conflicts occuring
            // between cousin packages

            var acceptedLibraries = Cache<TItem>.RentDictionary();

            var patience = 1000;
            var incomplete = true;

            var tracker = Cache<TItem>.RentTracker();

            var centralTransitiveNodes = root.InnerNodes.Where(n => n.Item.IsCentralTransitive).ToList();
            var hasCentralTransitiveDependencies = centralTransitiveNodes.Count > 0;

            while (incomplete && --patience != 0)
            {
                // Create a picture of what has not been rejected yet
                foreach (var node in root.EnumerateAllInTopologicalOrder())
                {
                    WalkTreeRejectNodesOfRejectedNodes(node, tracker);
                }

                if (hasCentralTransitiveDependencies)
                {
                    // Some of the central transitive nodes may be rejected now because their parents were rejected
                    // Reject them accordingly
                    root.RejectCentralTransitiveBecauseOfRejectedParents(tracker, centralTransitiveNodes);
                }

                foreach (var node in root.EnumerateAll().Where(x => x.Disposition != Disposition.Rejected))
                {
                    tracker.Track(node.Item);
                }

                foreach (var node in root.EnumerateAllInTopologicalOrder())
                {
                    WalkTreeAcceptOrRejectNodes(node, CreateState(tracker, acceptedLibraries));
                }

                incomplete = root.EnumerateAll().Any(x => x.Disposition == Disposition.Acceptable);

                tracker.Clear();
            }

            Cache<TItem>.ReleaseTracker(tracker);

            foreach (var node in root.EnumerateAll())
            {
                WalkTreeDectectConflicts(node, versionConflicts, acceptedLibraries);
            }

            Cache<TItem>.ReleaseDictionary(acceptedLibraries);

            return !incomplete;
        }

        private static void WalkTreeDectectConflicts<TItem>(GraphNode<TItem> node,  List<VersionConflictResult<TItem>> versionConflicts, Dictionary<string, GraphNode<TItem>> acceptedLibraries)
        {
            if (node.Disposition != Disposition.Accepted)
            {
                return;
            }

            // For all accepted nodes, find dependencies that aren't satisfied by the version
            // of the package that we have selected
            var innerNodes = node.InnerNodes;
            var count = innerNodes.Count;
            for (var i = 0; i < count; i++)
            {
                var childNode = innerNodes[i];
                GraphNode<TItem> acceptedNode;
                if (acceptedLibraries.TryGetValue(childNode.Key.Name, out acceptedNode) &&
                    childNode != acceptedNode &&
                    childNode.Key.VersionRange != null &&
                    acceptedNode.Item.Key.Version != null)
                {
                    var acceptedType = LibraryDependencyTargetUtils.Parse(acceptedNode.Item.Key.Type);
                    var childType = childNode.Key.TypeConstraint;

                    // Skip the check if a project reference override a package dependency
                    // Check the type constraints, if there is any overlap check for conflict
                    if ((acceptedType & (LibraryDependencyTarget.Project | LibraryDependencyTarget.ExternalProject)) == LibraryDependencyTarget.None
                        && (childType & acceptedType) != LibraryDependencyTarget.None)
                    {
                        var versionRange = childNode.Key.VersionRange;
                        var checkVersion = acceptedNode.Item.Key.Version;

                        if (!versionRange.Satisfies(checkVersion))
                        {
                            versionConflicts.Add(new VersionConflictResult<TItem>
                            {
                                Selected = acceptedNode,
                                Conflicting = childNode
                            });
                        }
                    }
                }
            }
        }

        private static void WalkTreeRejectNodesOfRejectedNodes<TItem>(GraphNode<TItem> node, Tracker<TItem> context)
        {
            if (node.OuterNodes.Count > 0 && node.OuterNodes.All(x=>x.Disposition == Disposition.Rejected))
            {
                // Mark all nodes as rejected if they aren't already marked
                node.Disposition = Disposition.Rejected;
            }
        }

        private static bool WalkTreeAcceptOrRejectNodes<TItem>(GraphNode<TItem> node, TrackerAndAccepted<TItem> context)
        {
            var tracker = context.Tracker;
            var acceptedLibraries = context.AcceptedLibraries;

            if (node.ParentNodes.Count > 0 && node.ParentNodes.All(x => x.Disposition != Disposition.Accepted))
            {
                return false;
            }
            else if (node.OuterNodes.Count > 0 && node.OuterNodes.All(x => x.Disposition != Disposition.Accepted))
            {
                return false;
            }

            if (node.Disposition == Disposition.Acceptable)
            {
                if (tracker.IsBestVersion(node.Item))
                {
                    node.Disposition = Disposition.Accepted;
                    acceptedLibraries[node.Key.Name] = node;
                }
                else
                {
                    node.Disposition = Disposition.Rejected;
                }
            }

            return node.Disposition == Disposition.Accepted;
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

        private static IEnumerable<GraphNode<TItem>> EnumerateAll<TItem>(this GraphNode<TItem> root)
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

        private static IEnumerable<GraphNode<TItem>> EnumerateAllInTopologicalOrder<TItem>(this GraphNode<TItem> root)
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

        [ThreadStatic]
        private static Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>> _tempDowngrades;

        public static Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>> RentDowngradesDictionary()
        {
            var dictionary = _tempDowngrades;
            if (dictionary != null)
            {
                _tempDowngrades = null;
                return dictionary;
            }

            return new Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>>();
        }

        public static void ReleaseDowngradesDictionary(Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>> dictionary)
        {
            if (_tempDowngrades == null)
            {
                dictionary.Clear();
                _tempDowngrades = dictionary;
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

        private struct TrackerAndAccepted<TItem>
        {
            public Tracker<TItem> Tracker;
            public Dictionary<string, GraphNode<TItem>> AcceptedLibraries;
        }

        private static TrackerAndAccepted<TItem> CreateState<TItem>(Tracker<TItem> tracker, Dictionary<string, GraphNode<TItem>> acceptedLibraries)
        {
            return new TrackerAndAccepted<TItem>
            {
                Tracker = tracker,
                AcceptedLibraries = acceptedLibraries
            };
        }

        private struct CyclesAndDowngrades
        {
            public List<GraphNode<RemoteResolveResult>> Cycles;
            public Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>> Downgrades;
        }

        private static CyclesAndDowngrades CreateState(List<GraphNode<RemoteResolveResult>> cycles, Dictionary<GraphNode<RemoteResolveResult>, GraphNode<RemoteResolveResult>> downgrades)
        {
            return new CyclesAndDowngrades
            {
                Cycles = cycles,
                Downgrades = downgrades
            };
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
