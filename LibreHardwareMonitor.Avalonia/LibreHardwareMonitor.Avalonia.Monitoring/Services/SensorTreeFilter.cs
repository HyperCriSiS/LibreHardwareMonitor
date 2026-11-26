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
    /// <param name="hiddenSensors">The identifiers of sensors to leave out, or null to keep all sensors.</param>
    /// <param name="groupSortOrders">Orders that replace <paramref name="sortOrder" /> below specific nodes, keyed by node identifier.</param>
    /// <param name="includeAdvanced">Whether sensors that are hidden by default are kept.</param>
    /// <returns>The filtered and sorted root nodes.</returns>
    public static IReadOnlyList<SensorTreeNodeSnapshot> Apply(IReadOnlyList<SensorTreeNodeSnapshot> roots,
        string text,
        SensorSortOrder sortOrder,
        IReadOnlySet<string> hiddenSensors = null,
        IReadOnlyDictionary<string, SensorSortOrder> groupSortOrders = null,
        bool includeAdvanced = true)
    {
        if (string.IsNullOrWhiteSpace(text) && sortOrder == SensorSortOrder.Default && !(hiddenSensors?.Count > 0) && !(groupSortOrders?.Count > 0) && includeAdvanced)
        {
            return roots;
        }

        return roots.Select(root => Apply(root, text?.Trim(), false, sortOrder, hiddenSensors, groupSortOrders, includeAdvanced)).ToList();
    }

    /// <summary>
    /// Filters and sorts a node and its children.
    /// </summary>
    /// <param name="node">The node to process.</param>
    /// <param name="text">The text to search, or empty to keep all sensors.</param>
    /// <param name="isMatched">Whether a parent node already matches the text.</param>
    /// <param name="sortOrder">The order of the sensors within their parent.</param>
    /// <param name="hiddenSensors">The identifiers of sensors to leave out, or null to keep all sensors.</param>
    /// <param name="groupSortOrders">Orders that replace <paramref name="sortOrder" /> below specific nodes.</param>
    /// <param name="includeAdvanced">Whether sensors that are hidden by default are kept.</param>
    /// <returns>The processed node, or null if neither it nor its children match.</returns>
    private static SensorTreeNodeSnapshot Apply(SensorTreeNodeSnapshot node,
        string text,
        bool isMatched,
        SensorSortOrder sortOrder,
        IReadOnlySet<string> hiddenSensors,
        IReadOnlyDictionary<string, SensorSortOrder> groupSortOrders,
        bool includeAdvanced)
    {
        if (groupSortOrders != null && groupSortOrders.TryGetValue(node.Identifier, out SensorSortOrder groupSortOrder))
        {
            sortOrder = groupSortOrder;
        }

        // The computer name is not searched, because it would match every sensor.
        isMatched = isMatched ||
                    string.IsNullOrEmpty(text) ||
                    node.Kind != SensorTreeNodeKind.Computer &&
                    node.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);

        if (node.Kind == SensorTreeNodeKind.Sensor)
        {
            return isMatched && hiddenSensors?.Contains(node.Identifier) != true && (includeAdvanced || !node.IsAdvanced) ? node : null;
        }

        IEnumerable<SensorTreeNodeSnapshot> children = node.Children
                                                           .Select(child => Apply(child, text, isMatched, sortOrder, hiddenSensors, groupSortOrders, includeAdvanced))
                                                           .Where(child => child != null);

        children = sortOrder switch
        {
            SensorSortOrder.Name => children.OrderBy(child => child.Kind == SensorTreeNodeKind.Sensor ? child.Name : string.Empty,
                StringComparer.CurrentCultureIgnoreCase),
            SensorSortOrder.ValueDescending => SortByValue(children, child => child.Value, true),
            SensorSortOrder.ValueAscending => SortByValue(children, child => child.Value, false),
            SensorSortOrder.MinDescending => SortByValue(children, child => child.Min, true),
            SensorSortOrder.MinAscending => SortByValue(children, child => child.Min, false),
            SensorSortOrder.MaxDescending => SortByValue(children, child => child.Max, true),
            SensorSortOrder.MaxAscending => SortByValue(children, child => child.Max, false),
            SensorSortOrder.AverageDescending => SortByValue(children, child => child.Average, true),
            SensorSortOrder.AverageAscending => SortByValue(children, child => child.Average, false),
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
            node.Average,
            node.IsAdvanced);
    }

    /// <summary>
    /// Orders sensors by a value. Other nodes keep their order before the sensors, missing values go last.
    /// </summary>
    private static IEnumerable<SensorTreeNodeSnapshot> SortByValue(IEnumerable<SensorTreeNodeSnapshot> children,
        Func<SensorTreeNodeSnapshot, float?> value,
        bool descending)
    {
        IOrderedEnumerable<SensorTreeNodeSnapshot> ordered = children.OrderBy(child => child.Kind == SensorTreeNodeKind.Sensor)
                                                                     .ThenBy(child => child.Kind == SensorTreeNodeKind.Sensor && !IsNumber(value(child)));

        return descending
            ? ordered.ThenByDescending(child => child.Kind == SensorTreeNodeKind.Sensor && IsNumber(value(child)) ? value(child).Value : 0)
            : ordered.ThenBy(child => child.Kind == SensorTreeNodeKind.Sensor && IsNumber(value(child)) ? value(child).Value : 0);
    }

    /// <summary>
    /// Gets whether a value is available and a number.
    /// </summary>
    private static bool IsNumber(float? value)
    {
        return value.HasValue && !float.IsNaN(value.Value);
    }
}
