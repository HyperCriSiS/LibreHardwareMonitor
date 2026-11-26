// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Avalonia.Data.Converters;

namespace LibreHardwareMonitor.Avalonia.UI.Converters;

/// <summary>
/// Dims the names of hidden sensors.
/// </summary>
public sealed class HiddenOpacityConverter : IValueConverter
{
    /// <summary>
    /// Gets the shared converter instance.
    /// </summary>
    public static HiddenOpacityConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? 0.5 : 1.0;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
