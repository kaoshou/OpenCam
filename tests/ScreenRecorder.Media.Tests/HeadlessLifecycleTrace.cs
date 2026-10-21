// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ScreenRecorder.Media.Tests;

internal static class HeadlessLifecycleTrace
{
    private static readonly object Gate = new();
    private static int _entries;
    private static readonly FieldInfo DispatcherField = typeof(Dispatcher).GetField("s_uiThread", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo JobsMethod = typeof(Dispatcher).GetMethod("GetJobs", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo CallbackField = typeof(DispatcherOperation).GetField("Callback", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LocatorProperty = typeof(AvaloniaLocator).GetProperty("Current")!;
    private static readonly MethodInfo ServiceMethod = typeof(AvaloniaLocator).GetMethod("GetService")!;
    internal sealed record Snapshot(bool FontService, bool Application, string[] Pending);

    // Inspect only the existing dispatcher. Do not create, run, abort or clear work.
    // Reflection is pinned to the tested Avalonia 11.2.5 diagnostics surface.
    internal static Snapshot Capture()
    {
        var dispatcher = (Dispatcher?)DispatcherField.GetValue(null);
        var jobs = dispatcher is null ? [] : (List<DispatcherOperation>)JobsMethod.Invoke(dispatcher, null)!;
        var locator = LocatorProperty.GetValue(null);
        return new(ServiceMethod.Invoke(locator, [typeof(IFontManagerImpl)]) is not null,
            ServiceMethod.Invoke(locator, [typeof(Application)]) is not null,
            jobs.Take(8).Select(job =>
            {
                var callback = (CallbackField.GetValue(job) as Delegate)?.Method;
                var name = $"{callback?.DeclaringType?.FullName}.{callback?.Name} [{job.Priority}]";
                return name[..Math.Min(name.Length, 200)];
            }).ToArray());
    }

    internal static void Record(string phase, MethodInfo method)
    {
        var path = Environment.GetEnvironmentVariable("OPENCAM_TEST_UI_TRACE");
        if (string.IsNullOrEmpty(path)) return;
        lock (Gate)
        {
            if (_entries >= 4096) return;
            var name = $"{method.DeclaringType?.FullName}.{method.Name}";
            var record = new { Sequence = ++_entries, Phase = phase, Method = name[..Math.Min(name.Length, 200)],
                Thread = Environment.CurrentManagedThreadId, State = Capture() };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, System.Text.Json.JsonSerializer.Serialize(record) + Environment.NewLine);
        }
    }
}
