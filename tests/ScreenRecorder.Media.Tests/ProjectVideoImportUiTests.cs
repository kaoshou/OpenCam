// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public class ProjectVideoImportUiTests
{
    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    [InlineData(AppLanguage.JaJp)]
    public void EmptyEditorCanImportAndLocksEditingWhileImportRuns(AppLanguage language)
    {
        var previous = LanguageManager.Instance.CurrentLanguage;
        LanguageManager.Instance.CurrentLanguage = language;
        var vm = new ProjectWorkspaceViewModel(new UnusedClient());
        var state = new ProjectSnapshot(Guid.NewGuid(), "Lesson", "/example/project", 0, 0,
            ProjectMode.Ready, 0, false, false) { ServerInstanceId = Guid.NewGuid() };
        vm.ApplyReply(new(true, null, state));
        var window = new ProjectWorkspaceView { DataContext = vm };
        try
        {
            window.Show(); window.UpdateLayout();
            var button = window.FindControl<Button>("ImportVideoButton")!;
            Assert.True(button.IsEnabled);
            Assert.True(button.Bounds.Width >= 100 && button.Bounds.Height >= 30);
            Assert.NotEqual("VideoImportHint", ToolTip.GetTip(button));
            vm.ApplyReply(new(true, null, state with { VideoImport = new(Guid.NewGuid(),
                ProjectVideoImportState.Running, "Converting", "Lesson") }));
            Assert.False(button.IsEnabled);
            Assert.False(vm.CanRecord);
            Assert.False(vm.CanSaveProject);
            Assert.False(vm.CanClose);
            Assert.DoesNotContain("VideoImportConverting", vm.ImportStatusText);
            vm.ApplyReply(new(true, null, state with { VideoImport = new(Guid.NewGuid(),
                ProjectVideoImportState.Canceled, "Done", "Lesson") }));
            Assert.True(button.IsEnabled);
            Assert.True(vm.CanRecord);
        }
        finally { window.Close(); LanguageManager.Instance.CurrentLanguage = previous; }
    }

    private sealed class UnusedClient : IProjectClient
    {
        public Task<ProjectReply> SendAsync(string command, ProjectRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProjectReply(true, null, ProjectSnapshot.Closed));
    }
}
