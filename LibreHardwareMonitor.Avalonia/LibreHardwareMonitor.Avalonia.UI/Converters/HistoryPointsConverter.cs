// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

namespace LibreHardwareMonitor.Avalonia.UI.Converters;

/// <summary>
/// Converts recent readings into the points of a history line.
/// </summary>
public sealed class HistoryPointsConverter : IValueConverter
{
    /// <summary>
    /// Gets the converter for the small line on dashboard tiles.
    /// </summary>
    public static HistoryPointsConverter Instance { get; } = new();

    /// <summary>
    /// Gets or sets the width of the line.
    /// </summary>
    public double Width { get; set; } = 196;

    /// <summary>
    /// Gets or sets the height of the line.
    /// </summary>
    public double Height { get; set; } = 36;

    /// <summary>
    /// Gets or sets how many readings fill the full width; fewer readings start further right. 0 stretches the
    /// available readings over the full width.
    /// </summary>
    public int Length { get; set; } = DashboardTileViewModel.HistoryLength;

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
        double step = Width / ((Length > 0 ? Math.Max(Length, history.Count) : history.Count) - 1);
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
