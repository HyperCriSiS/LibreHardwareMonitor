// SPDX-License-Identifier: MPL-2.0

using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Suggests what motherboard temperature sensors measure, from readings taken at idle and under CPU load.
/// </summary>
public static class SensorMappingAnalyzer
{
    /// <summary>
    /// Mean difference in degrees below which two sensors are considered to read the same value.
    /// </summary>
    private const float SameValueTolerance = 1.0f;

    /// <summary>
    /// Analyzes the motherboard temperatures of a recording.
    /// </summary>
    /// <param name="idle">Snapshots taken while the CPU was idle.</param>
    /// <param name="load">Snapshots taken while the CPU was fully loaded.</param>
    /// <returns>One suggestion per motherboard temperature sensor.</returns>
    public static IReadOnlyList<SensorMappingSuggestion> Analyze(IReadOnlyList<SensorTreeSnapshot> idle,
        IReadOnlyList<SensorTreeSnapshot> load)
    {
        List<SensorTreeSnapshot> all = idle.Concat(load).ToList();
        if (idle.Count == 0 || load.Count == 0)
        {
            return [];
        }

        // Every temperature with the hardware it belongs to, taken from the last snapshot.
        List<(SensorTreeNodeSnapshot Sensor, SensorTreeNodeSnapshot Hardware, SensorTreeNodeSnapshot Board)> temperatures = [];
        foreach (SensorTreeNodeSnapshot root in all[^1].Roots)
        {
            CollectTemperatures(root, null, null, temperatures);
        }

        Dictionary<string, float?[]> series = temperatures.ToDictionary(t => t.Sensor.Identifier,
            t => all.Select(snapshot => FindValue(snapshot, t.Sensor.Identifier)).ToArray());

        (SensorTreeNodeSnapshot Sensor, SensorTreeNodeSnapshot Hardware, SensorTreeNodeSnapshot Board) reference =
            temperatures.Where(t => t.Hardware.HardwareType == HardwareType.Cpu)
                        .OrderByDescending(t => t.Sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                                                t.Sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();

        float? referenceRise = reference.Sensor == null ? null : Rise(series[reference.Sensor.Identifier], idle.Count);

        List<SensorMappingSuggestion> suggestions = [];

        foreach ((SensorTreeNodeSnapshot sensor, SensorTreeNodeSnapshot hardware, SensorTreeNodeSnapshot board) in temperatures.Where(t => t.Board != null))
        {
            float?[] values = series[sensor.Identifier];
            List<float> valid = values.Where(IsPlausible).Select(v => v.Value).ToList();

            if (valid.Count < values.Length / 2)
            {
                suggestions.Add(new SensorMappingSuggestion(sensor.Identifier, sensor.Name, hardware.Name,
                    SensorMappingKind.NotConnected, null, null));
                continue;
            }

            float? rise = Rise(values, idle.Count);
            string sameAs = temperatures.Where(other => other.Sensor.Identifier != sensor.Identifier &&
                                                         other.Hardware.Identifier != hardware.Identifier &&
                                                         MeanDifference(values, series[other.Sensor.Identifier]) < SameValueTolerance)
                                        .Select(other => other.Hardware.Name + ": " + other.Sensor.Name)
                                        .FirstOrDefault();

            SensorMappingKind kind;
            if (valid.Max() - valid.Min() < 0.01f && valid.Count == values.Length)
            {
                kind = SensorMappingKind.Constant;
            }
            else if (referenceRise is not > 1.0f || rise == null)
            {
                kind = SensorMappingKind.Unknown;
            }
            else
            {
                float ratio = rise.Value / referenceRise.Value;
                kind = ratio switch
                {
                    > 0.6f => SensorMappingKind.FollowsCpu,
                    > 0.15f => SensorMappingKind.WarmsWithCpu,
                    _ => SensorMappingKind.IndependentOfCpu
                };
            }

            suggestions.Add(new SensorMappingSuggestion(sensor.Identifier, sensor.Name, hardware.Name, kind, rise, sameAs));
        }

        return suggestions;
    }

    /// <summary>
    /// Collects all temperature sensors and remembers their hardware and whether it belongs to the motherboard.
    /// </summary>
    private static void CollectTemperatures(SensorTreeNodeSnapshot node,
        SensorTreeNodeSnapshot hardware,
        SensorTreeNodeSnapshot board,
        List<(SensorTreeNodeSnapshot Sensor, SensorTreeNodeSnapshot Hardware, SensorTreeNodeSnapshot Board)> temperatures)
    {
        if (node.Kind == SensorTreeNodeKind.Hardware)
        {
            hardware = node;
            if (node.HardwareType == HardwareType.Motherboard)
            {
                board = node;
            }
        }

        if (node.Kind == SensorTreeNodeKind.Sensor && node.SensorType == SensorType.Temperature && hardware != null)
        {
            temperatures.Add((node, hardware, board));
        }

        foreach (SensorTreeNodeSnapshot child in node.Children)
        {
            CollectTemperatures(child, hardware, board, temperatures);
        }
    }

    /// <summary>
    /// Finds the value of a sensor in a snapshot.
    /// </summary>
    private static float? FindValue(SensorTreeSnapshot snapshot, string identifier)
    {
        Stack<SensorTreeNodeSnapshot> nodes = new(snapshot.Roots);
        while (nodes.Count > 0)
        {
            SensorTreeNodeSnapshot node = nodes.Pop();
            if (node.Kind == SensorTreeNodeKind.Sensor && node.Identifier == identifier)
            {
                return node.Value;
            }

            foreach (SensorTreeNodeSnapshot child in node.Children)
            {
                nodes.Push(child);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets whether a reading can be a real temperature. Open inputs typically read 0, -55, 127 or 255.
    /// </summary>
    private static bool IsPlausible(float? value)
    {
        return value is > 0.5f and < 120f and not 127f;
    }

    /// <summary>
    /// Gets the rise from the idle average to the average of the last third of the load phase.
    /// </summary>
    private static float? Rise(float?[] values, int idleCount)
    {
        List<float> idle = values.Take(idleCount).Where(IsPlausible).Select(v => v.Value).ToList();
        float?[] load = values.Skip(idleCount).ToArray();
        List<float> loaded = load.Skip(load.Length * 2 / 3).Where(IsPlausible).Select(v => v.Value).ToList();

        return idle.Count > 0 && loaded.Count > 0 ? loaded.Average() - idle.Average() : null;
    }

    /// <summary>
    /// Gets the mean absolute difference of two recordings, or infinity if they never overlap.
    /// </summary>
    private static float MeanDifference(float?[] first, float?[] second)
    {
        List<float> differences = first.Zip(second)
                                       .Where(pair => IsPlausible(pair.First) && IsPlausible(pair.Second))
                                       .Select(pair => Math.Abs(pair.First.Value - pair.Second.Value))
                                       .ToList();

        return differences.Count >= first.Length / 2 && differences.Count > 0 ? differences.Average() : float.PositiveInfinity;
    }
}
