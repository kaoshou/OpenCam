// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

public enum TeachingPointerStyle { WarmIvory, TealSilver, RoundShort, HighContrast }
public enum TeachingPointerSize { Small, Standard, Large }

public sealed record TeachingPointerOptions(
    TeachingPointerStyle Style = TeachingPointerStyle.WarmIvory,
    TeachingPointerSize Size = TeachingPointerSize.Standard)
{
    public static TeachingPointerOptions Default { get; } = new();
    public static TeachingPointerOptions Normalize(TeachingPointerOptions? options) => new(
        options is not null && Enum.IsDefined(options.Style) ? options.Style : TeachingPointerStyle.WarmIvory,
        options is not null && Enum.IsDefined(options.Size) ? options.Size : TeachingPointerSize.Standard);
    public double Scale => Size switch { TeachingPointerSize.Small => .82, TeachingPointerSize.Large => 1.23, _ => 1 };
}
