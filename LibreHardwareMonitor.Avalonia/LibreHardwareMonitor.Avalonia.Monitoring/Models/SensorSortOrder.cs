// SPDX-License-Identifier: MPL-2.0

namespace LibreHardwareMonitor.Avalonia.Monitoring.Models;

/// <summary>
/// Identifies how sensors are ordered within their parent node.
/// </summary>
public enum SensorSortOrder
{
    /// <summary>
    /// Sensors keep the order reported by the hardware.
    /// </summary>
    Default,

    /// <summary>
    /// Sensors are ordered alphabetically by name.
    /// </summary>
    Name,

    /// <summary>
    /// Sensors with the highest value come first.
    /// </summary>
    ValueDescending
}
