// SPDX-License-Identifier: MPL-2.0

using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Rearranges sensor tree snapshots without reading the hardware again.
/// </summary>
public static class SensorTreeGrouping
{
    /// <summary>
    /// Arranges the sensors of a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot grouped by hardware.</param>
    /// <param name="grouping">The requested arrangement.</param>
    /// <returns>The root nodes in the requested arrangement.</returns>
    public static IReadOnlyList<SensorTreeNodeSnapshot> GetRoots(SensorTreeSnapshot snapshot, SensorGrouping grouping)
    {
        if (grouping == SensorGrouping.Hardware)
        {
            return snapshot.Roots;
        }

        return snapshot.Roots
                       .Select(root => WithChildren(root, root.Identifier, GroupBySensorType(root)))
                       .ToList();
    }

    /// <summary>
    /// Creates one category for each measurement type, containing the hardware that provides it.
    /// </summary>
    /// <param name="root">The root node grouped by hardware.</param>
    /// <returns>The category nodes ordered by measurement type.</returns>
    private static IReadOnlyList<SensorTreeNodeSnapshot> GroupBySensorType(SensorTreeNodeSnapshot root)
    {
        SortedDictionary<SensorType, List<SensorTreeNodeSnapshot>> hardwareByType = new();

        Collect(root, hardwareByType);

        return hardwareByType.Select(group => new SensorTreeNodeSnapshot("type/" + group.Key,
                                                  SensorCategoryName.GetName(group.Key),
                                                  SensorTreeNodeKind.Category,
                                                  null,
                                                  group.Key,
                                                  null,
                                                  null,
                                                  null,
                                                  group.Value))
                             .ToList();
    }

    /// <summary>
    /// Collects the sensors of a hardware node and all its child devices by measurement type.
    /// </summary>
    /// <param name="parent">The node whose children are collected.</param>
    /// <param name="hardwareByType">The hardware nodes collected for each measurement type.</param>
    private static void Collect(SensorTreeNodeSnapshot parent,
        SortedDictionary<SensorType, List<SensorTreeNodeSnapshot>> hardwareByType)
    {
        foreach (SensorTreeNodeSnapshot child in parent.Children)
        {
            if (child.Kind != SensorTreeNodeKind.Category)
            {
                Collect(child, hardwareByType);
                continue;
            }

            SensorType sensorType = child.SensorType.Value;

            if (!hardwareByType.TryGetValue(sensorType, out List<SensorTreeNodeSnapshot> hardware))
            {
                hardwareByType.Add(sensorType, hardware = new List<SensorTreeNodeSnapshot>());
            }

            hardware.Add(WithChildren(parent, "type/" + child.Identifier, child.Children));
        }
    }

    /// <summary>
    /// Copies a node and replaces its identifier and children.
    /// </summary>
    /// <param name="node">The node to copy.</param>
    /// <param name="identifier">The identifier of the copy.</param>
    /// <param name="children">The children of the copy.</param>
    /// <returns>The copied node.</returns>
    private static SensorTreeNodeSnapshot WithChildren(SensorTreeNodeSnapshot node,
        string identifier,
        IReadOnlyList<SensorTreeNodeSnapshot> children)
    {
        return new SensorTreeNodeSnapshot(identifier,
            node.Name,
            node.Kind,
            node.HardwareType,
            node.SensorType,
            node.Value,
            node.Min,
            node.Max,
            children,
            node.Properties);
    }
}
