// SPDX-License-Identifier: AGPL-3.0-or-later
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

export function summarizeLog(text) {
  const measurements = new Map();
  for (const line of text.split(/\r?\n/)) {
    const marker = 'Recording performance: ';
    const offset = line.indexOf(marker);
    if (offset < 0) continue;
    let value;
    try { value = JSON.parse(line.slice(offset + marker.length)); } catch { continue; }
    const c = value?.Context, m = value?.Metrics;
    if (typeof c?.MeasurementId !== 'string' || !m || !['Starting','Progress','Stopped'].includes(value.Phase)) continue;
    measurements.set(c.MeasurementId, {
      measurement: c.MeasurementId, completed: value.Phase === 'Stopped',
      requestedCapture: c.RequestedCapture, actualCapture: c.ActualCapture, fallback: c.Fallback,
      requestedFps: c.RequestedFps, width: c.Width, height: c.Height, encoder: c.Encoder,
      synthetic: c.Synthetic, boundOutput: c.BoundOutput,
      elapsedSeconds: m.ElapsedSeconds, frames: m.Frames, averageOutputFps: m.OutputFps,
      ffmpegFps: m.FfmpegFps, speed: m.Speed, mediaSeconds: m.MediaSeconds,
      duplicatedFrames: m.DuplicatedFrames, droppedFrames: m.DroppedFrames,
      outputBytes: m.OutputBytes, writeWaitMs: m.WriteWaitMs, maxWriteWaitMs: m.MaxWriteWaitMs
    });
  }
  return [...measurements.values()];
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.length < 3) throw new Error('Usage: node scripts/summarize-recording-performance.mjs app-YYYYMMDD.log [...]');
  console.log(JSON.stringify(process.argv.slice(2).flatMap(file => summarizeLog(fs.readFileSync(file, 'utf8'))), null, 2));
}
