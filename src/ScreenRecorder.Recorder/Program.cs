using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.State;
using ScreenRecorder.Infrastructure.Diagnostics;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Infrastructure.Logging;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Media.Remux;
using ScreenRecorder.Recorder.Services;
using Serilog;

namespace ScreenRecorder.Recorder;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "ScreenRecorder", 
            "Logs");
        
        Log.Logger = LoggingConfiguration.CreateGlobalLogger(appDataDir);
        Log.Information("=== ScreenRecorder.Recorder 主行程啟動 ===");

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "未處理的全域例外 UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Log.Error(e.Exception, "未觀察到的非同步任務例外 UnobservedTaskException");
            e.SetObserved();
        };

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            using var serviceProvider = services.BuildServiceProvider();

            var orchestrator = serviceProvider.GetRequiredService<RecordingOrchestrator>();
            var displayService = serviceProvider.GetRequiredService<IDisplayService>();

            var pipeName = NamedPipeConstants.PipeBaseName;
            var pipeIndex = Array.IndexOf(args, "--pipe");
            if (pipeIndex >= 0 && pipeIndex < args.Length - 1)
            {
                pipeName = args[pipeIndex + 1];
            }

            var cts = new CancellationTokenSource();
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                cts.Cancel();
            };

            await using var ipcServer = new NamedPipeIpcServer(pipeName, async message =>
            {
                Log.Debug("收到 IPC 訊息: {MessageType}", message.MessageType);

                try
                {
                    switch (message.MessageType)
                    {
                        case "Ping":
                            return new IpcResponse { Success = true };

                        case "Shutdown":
                            Log.Information("收到 UI 的 Shutdown 訊息，即將正常結束 Recorder 行程");
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(200);
                                cts.Cancel();
                            });
                            return new IpcResponse { Success = true };

                        case "GetMonitors":
                            var monitors = displayService.GetMonitors();
                            return new IpcResponse 
                            { 
                                Success = true, 
                                ErrorMessage = JsonSerializer.Serialize(monitors) 
                            };

                        case "StartRecording":
                            var config = JsonSerializer.Deserialize<RecordingConfiguration>(message.PayloadJson) ?? new RecordingConfiguration();
                            var (startSuccess, startError, sessionId) = await orchestrator.StartRecordingAsync(config);
                            return new IpcResponse
                            {
                                Success = startSuccess,
                                ErrorMessage = startError,
                                SessionId = sessionId
                            };

                        case "PauseRecording":
                            var (pauseSuccess, pauseError) = await orchestrator.PauseRecordingAsync();
                            return new IpcResponse
                            {
                                Success = pauseSuccess,
                                ErrorMessage = pauseError
                            };

                        case "ResumeRecording":
                            var (resumeSuccess, resumeError) = await orchestrator.ResumeRecordingAsync();
                            return new IpcResponse
                            {
                                Success = resumeSuccess,
                                ErrorMessage = resumeError
                            };
                        case "UpdateAudioDevice":
                            var updateConfig = JsonSerializer.Deserialize<RecordingConfiguration>(message.PayloadJson);
                            if (updateConfig != null)
                            {
                                orchestrator.UpdateAudioConfiguration(updateConfig.SystemAudioDeviceId, updateConfig.MicrophoneDeviceId);
                                return new IpcResponse { Success = true };
                            }
                            return new IpcResponse { Success = false, ErrorMessage = "Invalid Payload" };

                        case "GetTelemetry":
                            var telemetry = orchestrator.GetTelemetry();
                            return new IpcResponse
                            {
                                Success = true,
                                ErrorMessage = JsonSerializer.Serialize(telemetry)
                            };

                        case "StopRecording":
                            var (stopSuccess, stopError, finalFilePath) = await orchestrator.StopRecordingAsync("UI 請求停止錄影");
                            return new IpcResponse
                            {
                                Success = stopSuccess,
                                ErrorMessage = stopError,
                                SessionId = finalFilePath
                            };

                        default:
                            return new IpcResponse { Success = false, ErrorMessage = "Unknown MessageType" };
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "處理 IPC 訊息時發生例外: {MessageType}", message.MessageType);
                    return new IpcResponse { Success = false, ErrorMessage = ex.Message };
                }
            });

            ipcServer.Start();
            Log.Information("IPC Server 已啟動，等待 UI 連線... Pipe: {PipeName}", pipeName);

            // 監聽父行程 PID，確保 UI 意外關閉或被 End Task 時 Recorder 自動收尾退出，不殘留背景進程
            var parentPidIndex = Array.IndexOf(args, "--parent-pid");
            if (parentPidIndex >= 0 && parentPidIndex < args.Length - 1 && int.TryParse(args[parentPidIndex + 1], out var parentPid))
            {
                try
                {
                    var parentProc = System.Diagnostics.Process.GetProcessById(parentPid);
                    parentProc.EnableRaisingEvents = true;
                    parentProc.Exited += (s, e) =>
                    {
                        Log.Information("監聽到父行程 UI (PID: {Pid}) 結束，Recorder 進行安全清理並退出", parentPid);
                        cts.Cancel();
                    };
                    Log.Information("已成功綁定守護父行程 (PID: {Pid})", parentPid);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "無法監聽父行程 PID {ParentPid}，可能父行程已先退出", parentPid);
                    cts.Cancel();
                }
            }

            if (args.Contains("--milestone2"))
            {
                Log.Information("Milestone 2 模式: 執行自動化測試 5 秒後結束");
                await Task.Delay(5000, cts.Token);
            }
            else
            {
                Log.Information("Milestone 2 模式: 自檢完成，進入常駐等待");
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException) { }
            }

            if (orchestrator.CurrentState == RecordingState.Recording)
            {
                Log.Warning("行程退出時偵測到錄影仍在進行，正在執行緊急停止並安全保存工作檔...");
                await orchestrator.StopRecordingAsync("主控行程或父行程關閉");
            }

            Log.Information("=== ScreenRecorder.Recorder 正常退出 ===");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "ScreenRecorder.Recorder 發生致命啟動例外");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IRecordingStateMachine, RecordingStateMachine>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<IRecordingSessionStore, JsonRecordingSessionStore>();
        services.AddSingleton<IDiskSpaceMonitor, DiskSpaceMonitor>();
        services.AddSingleton<IStreamCopyRemuxer, StreamCopyRemuxer>();
        services.AddSingleton<IMediaProbeService, MediaFileProbe>();
        services.AddSingleton<RecordingOrchestrator>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IDisplayService, ScreenRecorder.Platform.Windows.Display.WindowsDisplayService>();
            services.AddSingleton<IDisplayChangeMonitor, ScreenRecorder.Platform.Windows.Display.WindowsDisplayChangeMonitor>();
            services.AddSingleton<ICursorHighlightService, ScreenRecorder.Platform.Windows.Cursor.WindowsCursorHighlightService>();
            services.AddSingleton<IAudioDeviceService, ScreenRecorder.Platform.Windows.Audio.WindowsAudioDeviceService>();
            services.AddSingleton<IFFmpegPlatformProvider, ScreenRecorder.Platform.Windows.WindowsFFmpegProvider>();
            services.AddSingleton<ISystemAudioLoopbackCapture, ScreenRecorder.Platform.Windows.Audio.WindowsWasapiLoopbackCapture>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IDisplayService, ScreenRecorder.Platform.macOS.MacOsDisplayService>();
            services.AddSingleton<IDisplayChangeMonitor, ScreenRecorder.Platform.macOS.MacOsDisplayChangeMonitor>();
            services.AddSingleton<ICursorHighlightService, ScreenRecorder.Platform.macOS.MacOsCursorHighlightService>();
            services.AddSingleton<IAudioDeviceService, ScreenRecorder.Platform.macOS.MacOsAudioDeviceService>();
            services.AddSingleton<IFFmpegPlatformProvider, ScreenRecorder.Platform.macOS.MacOsFFmpegProvider>();
            services.AddSingleton<ISystemAudioLoopbackCapture, ScreenRecorder.Platform.macOS.MacOsAudioLoopbackCapture>();
        }
    }
}
