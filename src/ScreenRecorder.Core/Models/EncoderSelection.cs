// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;

namespace ScreenRecorder.Core.Models;

public enum EncoderFallbackReason { None, NoValidatedHardware, RequestedHardwareUnavailable, HardwareStartupFailed }

public sealed record EncoderSelection(HardwareEncoderType Encoder, EncoderFallbackReason FallbackReason);
