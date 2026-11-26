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
    ValueDescending,

    /// <summary>
    /// Sensors with the lowest value come first.
    /// </summary>
    ValueAscending,

    /// <summary>
    /// Sensors with the highest minimum come first.
    /// </summary>
    MinDescending,

    /// <summary>
    /// Sensors with the lowest minimum come first.
    /// </summary>
    MinAscending,

    /// <summary>
    /// Sensors with the highest maximum come first.
    /// </summary>
    MaxDescending,

    /// <summary>
    /// Sensors with the lowest maximum come first.
    /// </summary>
    MaxAscending,

    /// <summary>
    /// Sensors with the highest average come first.
    /// </summary>
    AverageDescending,

    /// <summary>
    /// Sensors with the lowest average come first.
    /// </summary>
    AverageAscending
}
