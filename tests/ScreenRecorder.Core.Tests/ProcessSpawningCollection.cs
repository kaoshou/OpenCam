// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Tests;

// Unix process creation can temporarily retain other threads' open flock leases
// before close-on-exec runs. Do not let a probe fork overlap unrelated tests'
// close-and-immediate-reopen ownership assertions. All real cross-process lease
// assertions remain in ProjectStoreTests; product locking is unchanged.
[CollectionDefinition("Process spawning", DisableParallelization = true)]
public sealed class ProcessSpawningCollection { }
