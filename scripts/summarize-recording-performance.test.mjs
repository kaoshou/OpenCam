import {test} from 'node:test';
import assert from 'node:assert/strict';
import {summarizeLog} from './summarize-recording-performance.mjs';

test('summarizes each attempt using actual backend rather than requested mode', () => {
  const line = (id, phase, fps, actual) => 'timestamp [INF] Recording performance: ' + JSON.stringify({
    Phase: phase, Context: {MeasurementId: id, RequestedCapture: 'ModernExperimental', ActualCapture: actual,
      Fallback: actual === 'Gdi' ? 'AdapterIdentityUncertain' : 'None', RequestedFps: fps},
    Metrics: {Frames: 120, OutputFps: 24, DuplicatedFrames: null}
  });
  const report = summarizeLog([line('a','Starting',30,'Gdi'), line('b','Progress',60,'DesktopDuplication'), line('a','Stopped',30,'Gdi')].join('\n'));
  assert.equal(report.length, 2);
  assert.equal(report[0].actualCapture, 'Gdi');
  assert.equal(report[0].completed, true);
  assert.equal(report[0].fallback, 'AdapterIdentityUncertain');
  assert.equal(report[0].duplicatedFrames, null);
  assert.equal(report[1].requestedFps, 60);
  assert.equal(report[1].completed, false);
});

test('ignores unrelated or truncated log records', () => {
  assert.deepEqual(summarizeLog('private path\nRecording performance: {\nRecording performance: null'), []);
});
