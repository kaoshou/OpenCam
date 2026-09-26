// SPDX-License-Identifier: AGPL-3.0-or-later
using Xunit;

// These tests exercise process-wide state (localization, environment variables)
// and OS resources (FFmpeg, named pipes, audio devices). Running classes in
// parallel makes otherwise independent integration tests contend for those
// shared resources and produces platform-dependent startup timeouts.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
