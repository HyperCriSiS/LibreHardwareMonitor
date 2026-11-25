// SPDX-License-Identifier: MPL-2.0

using LibreHardwareMonitor.Avalonia.Monitoring.Models;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Filters and sorts the sensors of a tree without reading the hardware again.
/// </summary>
public static class SensorTreeFilter
{
    /// <summary>
    /// Keeps the sensors that match a text and orders them.
    /// </summary>
    /// <param name="roots">The root nodes to filter.</param>
    /// <param name="text">The text to search in sensor, category and hardware names, or empty to keep all sensors.</param>
    /// <param name="sortOrder">The order of the sensors within their parent.</param>
    /// <returns>The filtered and sorted root nodes.</returns>
    public static IReadOnlyList<SensorTreeNodeSnapshot> Apply(IReadOnlyList<SensorTreeNodeSnapshot> roots,
        string text,
        SensorSortOrder sortOrder)
    {
        if (string.IsNullOrWhiteSpace(text) && sortOrder == SensorSortOrder.Default)
        {
            return roots;
        }

        return roots.Select(root => Apply(root, text?.Trim(), false, sortOrder)).ToList();
    }

    /// <summary>
    /// Filters and sorts a node and its children.
    /// </summary>
    /// <param name="node">The node to process.</param>
    /// <param name="text">The text to search, or empty to keep all sensors.</param>
    /// <param name="isMatched">Whether a parent node already matches the text.</param>
    /// <param name="sortOrder">The order of the sensors within their parent.</param>
    /// <returns>The processed node, or null if neither it nor its children match.</returns>
    private static SensorTreeNodeSnapshot Apply(SensorTreeNodeSnapshot node,
        string text,
        bool isMatched,
        SensorSortOrder sortOrder)
    {
        // The computer name is not searched, because it would match every sensor.
        isMatched = isMatched ||
                    string.IsNullOrEmpty(text) ||
                    node.Kind != SensorTreeNodeKind.Computer &&
                    node.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);

        if (node.Kind == SensorTreeNodeKind.Sensor)
        {
            return isMatched ? node : null;
        }

        IEnumerable<SensorTreeNodeSnapshot> children = node.Children
                                                           .Select(child => Apply(child, text, isMatched, sortOrder))
                                                           .Where(child => child != null);

        children = sortOrder switch
        {
            SensorSortOrder.Name => children.OrderBy(child => child.Kind == SensorTreeNodeKind.Sensor ? child.Name : string.Empty,
                StringComparer.CurrentCultureIgnoreCase),
            SensorSortOrder.ValueDescending => children.OrderByDescending(child => child.Kind == SensorTreeNodeKind.Sensor ? child.Value ?? float.MinValue : float.MaxValue),
            _ => children
        };

        List<SensorTreeNodeSnapshot> result = children.ToList();

        if (result.Count == 0 && node.Kind != SensorTreeNodeKind.Computer)
        {
            return null;
        }

        return new SensorTreeNodeSnapshot(node.Identifier,
            node.Name,
            node.Kind,
            node.HardwareType,
            node.SensorType,
            node.Value,
            node.Min,
            node.Max,
            result,
            node.Properties,
            node.Average);
    }
}
