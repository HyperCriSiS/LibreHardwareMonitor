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
            return snapshot.Roots
                           .Select(root => WithChildren(root, root.Identifier, GroupHardware(root.Identifier, root.Children)))
                           .ToList();
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
                                                  GroupHardware("type/" + group.Key, group.Value)))
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
    /// Hardware families that are grouped when there is more than one device, with the property that divides them
    /// into subgroups and the order of those subgroups.
    /// </summary>
    private static readonly (HardwareType Type, string Key, string Property, string[] Kinds)[] Families =
    [
        (HardwareType.Network, "network", "Connection", ["Physical", "Virtual", "Filter"]),
        (HardwareType.Storage, "storage", "Drive type", ["SSD", "HDD", "Removable", "Virtual"]),
        (HardwareType.Memory, "memory", null, [])
    ];

    /// <summary>
    /// Puts network adapters and drives into groups, divided by connection or drive type. Other nodes keep their place.
    /// </summary>
    /// <param name="parentIdentifier">The identifier of the parent, used to make group identifiers unique.</param>
    /// <param name="children">The nodes to group.</param>
    /// <returns>The nodes with the groups in place of the first device of each family.</returns>
    private static IReadOnlyList<SensorTreeNodeSnapshot> GroupHardware(string parentIdentifier, IReadOnlyList<SensorTreeNodeSnapshot> children)
    {
        List<SensorTreeNodeSnapshot> result = [];
        HashSet<HardwareType> done = [];

        foreach (SensorTreeNodeSnapshot child in children)
        {
            int family = Array.FindIndex(Families, f => child.Kind == SensorTreeNodeKind.Hardware && f.Type == child.HardwareType);
            if (family < 0)
            {
                result.Add(child);
                continue;
            }

            if (!done.Add(child.HardwareType.Value))
            {
                continue;
            }

            (HardwareType type, string key, string property, string[] kinds) = Families[family];
            List<SensorTreeNodeSnapshot> members = children.Where(c => c.Kind == SensorTreeNodeKind.Hardware && c.HardwareType == type).ToList();

            if (members.Count == 1)
            {
                result.Add(child);
                continue;
            }

            string identifier = parentIdentifier + "/group/" + key;
            List<IGrouping<string, SensorTreeNodeSnapshot>> byKind = members.GroupBy(m => GetKind(m, property, kinds))
                                                                           .OrderBy(g => Array.IndexOf(kinds, g.Key) is var i and >= 0 ? i : kinds.Length)
                                                                           .ToList();

            IReadOnlyList<SensorTreeNodeSnapshot> groupChildren = byKind.Count == 1
                ? members
                : byKind.Select(g => CreateGroup(identifier + "/" + g.Key, SensorGroupName.GetKindName(g.Key), type, g.ToList())).ToList();

            result.Add(CreateGroup(identifier, SensorGroupName.GetFamilyName(type), type, groupChildren));
        }

        return result;
    }

    /// <summary>
    /// Gets the kind of a device from its properties, or "Other" if it is unknown.
    /// </summary>
    private static string GetKind(SensorTreeNodeSnapshot node, string property, string[] kinds)
    {
        return property != null && node.Properties != null && node.Properties.TryGetValue(property, out string kind) && kinds.Contains(kind) ? kind : "Other";
    }

    /// <summary>
    /// Creates a group node.
    /// </summary>
    private static SensorTreeNodeSnapshot CreateGroup(string identifier, string name, HardwareType type, IReadOnlyList<SensorTreeNodeSnapshot> children)
    {
        return new SensorTreeNodeSnapshot(identifier, name, SensorTreeNodeKind.Group, type, null, null, null, null, children);
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
            node.Properties,
            node.Average);
    }
}
