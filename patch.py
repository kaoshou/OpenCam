import re

# 1. Modify FFmpegScreenRecorderEngine.cs
engine_path = r'src\ScreenRecorder.Media\Capture\FFmpegScreenRecorderEngine.cs'
with open(engine_path, 'r', encoding='utf-8') as f:
    engine_content = f.read()

engine_content = engine_content.replace('public event EventHandler<string>? EngineErrorOccurred;', 'public event EventHandler<string>? EngineErrorOccurred;\n    public event EventHandler? AudioDeviceLost;')

search_str = 'var timeMatch = TimeRegex.Match(line);'
insert_str = '''
                if (line.Contains("real-time buffer too full", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("error capturing audio", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("audio device lost", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("device disconnected", StringComparison.OrdinalIgnoreCase))
                {
                    AudioDeviceLost?.Invoke(this, EventArgs.Empty);
                }

                var timeMatch = TimeRegex.Match(line);'''
engine_content = engine_content.replace(search_str, insert_str)

with open(engine_path, 'w', encoding='utf-8') as f:
    f.write(engine_content)

# 2. Modify RecordingOrchestrator.cs
orch_path = r'src\ScreenRecorder.Recorder\Services\RecordingOrchestrator.cs'
with open(orch_path, 'r', encoding='utf-8') as f:
    orch_content = f.read()

orch_content = orch_content.replace('_engine.EngineWarningOccurred += (s, warn) =>\n            {\n                Log.Warning("錄影引擎發出警告: {Warning}", warn);\n            };', '_engine.EngineWarningOccurred += (s, warn) =>\n            {\n                Log.Warning("錄影引擎發出警告: {Warning}", warn);\n            };\n            _engine.AudioDeviceLost += OnAudioDeviceLost;')
orch_content = orch_content.replace('_engine.EngineWarningOccurred += (s, warn) => Log.Warning("錄影引擎發出警告: {Warning}", warn);', '_engine.EngineWarningOccurred += (s, warn) => Log.Warning("錄影引擎發出警告: {Warning}", warn);\n            _engine.AudioDeviceLost += OnAudioDeviceLost;')

insert_orch = '''
    private bool _isRecoveringAudio = false;

    private async void OnAudioDeviceLost(object? sender, EventArgs e)
    {
        if (_isRecoveringAudio || _currentSession == null || _stateMachine.CurrentState != RecordingState.Recording) return;
        
        Log.Warning("偵測到音訊裝置拔除或異常崩潰，準備觸發安全靜音補償接續錄製...");
        _isRecoveringAudio = true;

        try
        {
            var session = _currentSession;
            session.Configuration.IsRecoverySilenceMode = true;

            if (_engine != null)
            {
                await _engine.StopRecordingAsync(default);
                _accumulatedDuration += _engine.CurrentRecordedTime;
                session.TotalVideoFramesRecorded += _engine.CurrentFramesRecorded;
            }

            _segmentIndex++;
            var nextSegment = Path.Combine(session.WorkingDirectory, $"segment_{_segmentIndex:D3}.mkv");
            session.SegmentFilePaths.Add(nextSegment);
            session.WorkingFilePath = nextSegment;

            var actualBounds = ResolveCaptureBounds(session.Configuration);

            _engine = new FFmpegScreenRecorderEngine(_ffmpegPlatformProvider)
            {
                UseSyntheticCaptureSource = UseSyntheticCaptureSource
            };
            _engine.EngineErrorOccurred += (s, err) => Log.Error("錄影引擎報告錯誤: {Error}", err);
            _engine.EngineWarningOccurred += (s, warn) => Log.Warning("錄影引擎發出警告: {Warning}", warn);
            _engine.AudioDeviceLost += OnAudioDeviceLost;

            await _engine.StartRecordingAsync(nextSegment, session.Configuration, actualBounds, default);
            
            await _sessionStore.SaveSessionAsync(session, default);
            
            Log.Information("已成功切換至靜音補償模式，繼續錄製分段 {Index}: {Path}", _segmentIndex, nextSegment);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "執行音訊熱拔插補償時失敗，錄影中斷");
            EmergencyStopTriggered?.Invoke(this, $"音訊裝置拔除後無法自動恢復錄影: {ex.Message}");
            await StopRecordingAsync("音訊裝置拔除恢復失敗");
        }
        finally
        {
            _isRecoveringAudio = false;
        }
    }

    private async void OnDiskSpaceCritical(object? sender, long remainingBytes)'''

orch_content = orch_content.replace('private async void OnDiskSpaceCritical(object? sender, long remainingBytes)', insert_orch)

with open(orch_path, 'w', encoding='utf-8') as f:
    f.write(orch_content)

print("Modification complete")
