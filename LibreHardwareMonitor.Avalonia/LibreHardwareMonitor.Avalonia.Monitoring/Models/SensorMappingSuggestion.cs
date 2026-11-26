// SPDX-License-Identifier: MPL-2.0

namespace LibreHardwareMonitor.Avalonia.Monitoring.Models;

/// <summary>
/// Describes how a motherboard temperature reacted during a mapping recording.
/// </summary>
public enum SensorMappingKind
{
    /// <summary>
    /// The CPU temperature did not rise enough to judge the sensor.
    /// </summary>
    Unknown,

    /// <summary>
    /// Most readings were implausible, which is typical for an unconnected input.
    /// </summary>
    NotConnected,

    /// <summary>
    /// The reading never changed.
    /// </summary>
    Constant,

    /// <summary>
    /// The sensor rose almost as much as the CPU, for example a CPU socket or CPU diode input.
    /// </summary>
    FollowsCpu,

    /// <summary>
    /// The sensor rose noticeably with CPU load, for example near the voltage regulators or the socket.
    /// </summary>
    WarmsWithCpu,

    /// <summary>
    /// The sensor barely reacted to CPU load, for example chipset, PCIe slot, case or an external probe.
    /// </summary>
    IndependentOfCpu
}

/// <summary>
/// Suggests what a motherboard temperature sensor measures.
/// </summary>
/// <param name="Identifier">The sensor identifier.</param>
/// <param name="Name">The current sensor name.</param>
/// <param name="HardwareName">The name of the chip that provides the sensor.</param>
/// <param name="Kind">How the sensor reacted to CPU load.</param>
/// <param name="Rise">The rise in degrees from idle to load, if measurable.</param>
/// <param name="SameValueAs">A sensor of another chip that reads the same value, if any.</param>
public sealed record SensorMappingSuggestion(string Identifier,
    string Name,
    string HardwareName,
    SensorMappingKind Kind,
    float? Rise,
    string SameValueAs);
