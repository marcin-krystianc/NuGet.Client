// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace NuGet.Commands
{
    internal class RestoreCommandCache
    {
        // Cache package data and selection criteria across graphs.
        public LockFileBuilderCache LockFileBuilderCache { get; set; } = new LockFileBuilderCache();
    }
}
