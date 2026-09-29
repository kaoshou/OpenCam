// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Interfaces;

public interface IEncoderSelectionService : IEncoderDetector
{
    Task<EncoderSelection> SelectAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default);
    void ReportStartupFailure(HardwareEncoderType encoder);
}
