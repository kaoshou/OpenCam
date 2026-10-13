using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using System.Text;

namespace ScreenRecorder.Recorder.Services;

public sealed class ProjectWebPlayerExporter(IRecordingContentExporter exporter) : IRecordingContentExporter
{
    public async Task<RecordingExportResult> ExportAsync(IProjectHandle owner, RecordingProject snapshot,
        string directory, Guid id, IProgress<double> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var data = ProjectWebPlayerDocument.CreateData(snapshot);
        if (data.Chapters.Length == 0) throw new InvalidOperationException("There is no content to export.");
        var html = ProjectWebPlayerDocument.CreateHtml(data);
        var script = ProjectWebPlayerDocument.CreateScript(data);
        using var parent = BoundDirectory.Open(directory);
        var folderName = $"OpenCam_{ProjectNaming.FileStem(snapshot.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{id:N}";
        using var folder = parent.OpenChild(folderName, create: true, exclusive: true);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        var complete = false;
        async Task Write(string name, string content)
        {
            await using var output = folder.CreateNew(name);
            owned.Add(name);
            await output.WriteAsync(Encoding.UTF8.GetBytes(content), ct);
            output.Flush(true);
        }
        void Publish(string temporary, string name)
        {
            folder.PublishVerified(temporary, name);
            owned.Remove(temporary);
            owned.Add(name);
        }
        try
        {
            var result = await exporter.ExportAsync(owner, snapshot, folder.CurrentPath, id, new MediaProgress(progress), ct);
            if (!result.Success || result.FinalPath is null) return result;
            using (var generatedDirectory = BoundDirectory.Open(Path.GetDirectoryName(result.FinalPath)!))
                if (!string.Equals(generatedDirectory.CurrentPath, folder.CurrentPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("Web export destination changed.");
            var generated = Path.GetFileName(result.FinalPath);
            using (var verified = folder.Read(generated))
                if (verified.Length == 0) throw new InvalidDataException("Web export has no verified media.");
            owned.Add(generated);
            ct.ThrowIfCancellationRequested();
            Publish(generated, "media.mp4");
            await Write(".opencam-player.js.part", script);
            await Write(".index.html.part", html);
            ct.ThrowIfCancellationRequested();
            Publish(".opencam-player.js.part", "opencam-player.js");
            // Publish the entry page last, only after the media and data are complete.
            Publish(".index.html.part", "index.html");
            var path = Path.Combine(folder.CurrentPath, "index.html");
            complete = true;
            progress.Report(1);
            return new(true, path, null);
        }
        finally
        {
            if (!complete)
                foreach (var name in owned)
                    try { folder.DeleteOwnedFile(name); }
                    catch (IOException ex) { Serilog.Log.Warning(ex, "Cannot clean owned web export file {Name}", name); }
                    catch (UnauthorizedAccessException ex) { Serilog.Log.Warning(ex, "Cannot clean owned web export file {Name}", name); }
        }
    }

    private sealed class MediaProgress(IProgress<double> progress) : IRecordingExportProgress
    {
        public void Report(double value) => progress.Report(value * .95);
        public void ReportPhase(RecordingExportPhase phase) => (progress as IRecordingExportProgress)?.ReportPhase(phase);
    }
}
