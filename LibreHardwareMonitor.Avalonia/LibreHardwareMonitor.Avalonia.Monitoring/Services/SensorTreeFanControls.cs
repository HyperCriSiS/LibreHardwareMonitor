// SPDX-License-Identifier: MPL-2.0

using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Hardware;
using LanguageResources = LibreHardwareMonitor.Avalonia.Localization.Resources.Resources;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Shows the duty cycle of a fan header below the fan speed, instead of in a separate "Controls" category.
/// </summary>
public static class SensorTreeFanControls
{
    /// <summary>
    /// Moves each control sensor below the fan sensor with the same index of the same hardware.
    /// Controls without a matching fan stay in their category.
    /// </summary>
    /// <param name="nodes">The nodes to process.</param>
    /// <returns>The processed nodes.</returns>
    public static IReadOnlyList<SensorTreeNodeSnapshot> Apply(IReadOnlyList<SensorTreeNodeSnapshot> nodes)
    {
        return nodes.Select(Apply).ToList();
    }

    /// <summary>
    /// Processes a node and its children.
    /// </summary>
    private static SensorTreeNodeSnapshot Apply(SensorTreeNodeSnapshot node)
    {
        if (node.Kind == SensorTreeNodeKind.Sensor)
        {
            return node;
        }

        List<SensorTreeNodeSnapshot> children = node.Children.Select(Apply).ToList();

        SensorTreeNodeSnapshot fans = children.FirstOrDefault(c => c.Kind == SensorTreeNodeKind.Category && c.SensorType == SensorType.Fan);
        SensorTreeNodeSnapshot controls = children.FirstOrDefault(c => c.Kind == SensorTreeNodeKind.Category && c.SensorType == SensorType.Control);

        if (node.Kind == SensorTreeNodeKind.Hardware && fans != null && controls != null)
        {
            Dictionary<string, SensorTreeNodeSnapshot> controlsByIndex = controls.Children.Where(c => GetIndex(c) != null)
                                                                                .GroupBy(GetIndex)
                                                                                .ToDictionary(g => g.Key, g => g.First());
            HashSet<string> moved = [];

            List<SensorTreeNodeSnapshot> fanSensors = fans.Children.Select(fan =>
            {
                if (GetIndex(fan) is { } index && controlsByIndex.TryGetValue(index, out SensorTreeNodeSnapshot control))
                {
                    moved.Add(control.Identifier);
                    return Copy(fan, [Copy(control, [], LanguageResources.FanDutyCycle)], fan.Name);
                }

                return fan;
            }).ToList();

            List<SensorTreeNodeSnapshot> remainingControls = controls.Children.Where(c => !moved.Contains(c.Identifier)).ToList();

            children = children.Select(c => c == fans ? Copy(fans, fanSensors, fans.Name) : c)
                               .Where(c => c != controls || remainingControls.Count > 0)
                               .Select(c => c == controls ? Copy(controls, remainingControls, controls.Name) : c)
                               .ToList();
        }

        return Copy(node, children, node.Name);
    }

    /// <summary>
    /// Gets the sensor index from an identifier such as "/lpc/nct6798d/0/fan/2".
    /// </summary>
    private static string GetIndex(SensorTreeNodeSnapshot sensor)
    {
        int slash = sensor.Identifier.LastIndexOf('/');
        return slash >= 0 && int.TryParse(sensor.Identifier.AsSpan(slash + 1), out _) ? sensor.Identifier.Substring(slash + 1) : null;
    }

    /// <summary>
    /// Copies a node with new children and name.
    /// </summary>
    private static SensorTreeNodeSnapshot Copy(SensorTreeNodeSnapshot node, IReadOnlyList<SensorTreeNodeSnapshot> children, string name)
    {
        return new SensorTreeNodeSnapshot(node.Identifier,
            name,
            node.Kind,
            node.HardwareType,
            node.SensorType,
            node.Value,
            node.Min,
            node.Max,
            children,
            node.Properties,
            node.Average,
            node.IsAdvanced);
    }
}
