// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Data.Converters;

namespace ScreenRecorder.UI.Projects.Editor;

public sealed class ClipDensityLabelConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values.Count == 3 && values[0] is bool compact
            ? values[compact ? 2 : 1] as string ?? "" : "";
}
