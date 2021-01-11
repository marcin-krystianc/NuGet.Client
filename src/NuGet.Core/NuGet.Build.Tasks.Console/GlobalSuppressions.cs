// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Build", "CA1031:Modify 'GetDependencyGraphSpec' to catch a more specific allowed exception type, or rethrow the exception.", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Build.Tasks.Console.MSBuildStaticGraphRestore.GetDependencyGraphSpec(System.String,System.Collections.Generic.IDictionary{System.String,System.String})~NuGet.ProjectModel.DependencyGraphSpec")]
[assembly: SuppressMessage("Build", "CA1031:Modify 'LoadProjects' to catch a more specific allowed exception type, or rethrow the exception.", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Build.Tasks.Console.MSBuildStaticGraphRestore.LoadProjects(System.Collections.Generic.IEnumerable{Microsoft.Build.Graph.ProjectGraphEntryPoint})~System.Collections.Generic.ICollection{NuGet.Build.Tasks.Console.ProjectWithInnerNodes}")]
[assembly: SuppressMessage("Build", "CA1031:Modify 'RestoreAsync' to catch a more specific allowed exception type, or rethrow the exception.", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Build.Tasks.Console.MSBuildStaticGraphRestore.RestoreAsync(System.String,System.Collections.Generic.IDictionary{System.String,System.String},System.Collections.Generic.IReadOnlyDictionary{System.String,System.String})~System.Threading.Tasks.Task{System.Boolean}")]
[assembly: SuppressMessage("Build", "CA1307:The behavior of 'string.Equals(string, string)' could vary based on the current user's locale settings. Replace this call in 'NuGet.Build.Tasks.Console.MSBuildFeatureFlags.EnableCacheFileEnumerations.get' with a call to 'string.Equals(string, string, System.StringComparison)'.", Justification = "<Pending>", Scope = "member", Target = "~P:NuGet.Build.Tasks.Console.MSBuildFeatureFlags.EnableCacheFileEnumerations")]
[assembly: SuppressMessage("Build", "CA1307:The behavior of 'string.Equals(string, string)' could vary based on the current user's locale settings. Replace this call in 'NuGet.Build.Tasks.Console.MSBuildFeatureFlags.LoadAllFilesAsReadonly.get' with a call to 'string.Equals(string, string, System.StringComparison)'.", Justification = "<Pending>", Scope = "member", Target = "~P:NuGet.Build.Tasks.Console.MSBuildFeatureFlags.LoadAllFilesAsReadonly")]
[assembly: SuppressMessage("Build", "CA1307:The behavior of 'string.Equals(string, string)' could vary based on the current user's locale settings. Replace this call in 'NuGet.Build.Tasks.Console.MSBuildFeatureFlags.SkipEagerWildcardEvaluations.get' with a call to 'string.Equals(string, string, System.StringComparison)'.", Justification = "<Pending>", Scope = "member", Target = "~P:NuGet.Build.Tasks.Console.MSBuildFeatureFlags.SkipEagerWildcardEvaluations")]
