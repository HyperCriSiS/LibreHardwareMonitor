// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

namespace LibreHardwareMonitor.Avalonia.UI.Converters;

/// <summary>
/// Converts the readings of a dashboard tile into the points of a small history line.
/// </summary>
public sealed class HistoryPointsConverter : IValueConverter
{
    /// <summary>
    /// The width of the history line.
    /// </summary>
    public const double Width = 196;

    /// <summary>
    /// The height of the history line.
    /// </summary>
    public const double Height = 36;

    /// <summary>
    /// Gets the shared converter instance.
    /// </summary>
    public static HistoryPointsConverter Instance { get; } = new();

    /// <summary>
    /// Scales the readings to the line, with the lowest reading at the bottom and the highest at the top.
    /// </summary>
    /// <param name="value">The readings, oldest first.</param>
    /// <param name="targetType">The target binding type.</param>
    /// <param name="parameter">An optional converter parameter.</param>
    /// <param name="culture">The binding culture.</param>
    /// <returns>The points of the line.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        List<Point> points = [];

        if (value is not IReadOnlyList<float> history || history.Count < 2)
            return points;

        float min = history.Min();
        float range = history.Max() - min;
        double step = Width / (DashboardTileViewModel.HistoryLength - 1);
        double start = Width - (step * (history.Count - 1));

        for (int i = 0; i < history.Count; i++)
        {
            // A flat line sits in the middle.
            double y = range > 0 ? Height - ((history[i] - min) / range * Height) : Height / 2;
            points.Add(new Point(start + (step * i), y));
        }

        return points;
    }

    /// <summary>
    /// Not supported, the conversion is one way.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
