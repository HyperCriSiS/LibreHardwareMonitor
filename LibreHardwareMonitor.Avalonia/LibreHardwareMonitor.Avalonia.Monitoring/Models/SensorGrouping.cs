// SPDX-License-Identifier: MPL-2.0

namespace LibreHardwareMonitor.Avalonia.Monitoring.Models;

/// <summary>
/// Identifies how sensors are arranged in the tree.
/// </summary>
public enum SensorGrouping
{
    /// <summary>
    /// Sensors are listed below the hardware that provides them.
    /// </summary>
    Hardware,

    /// <summary>
    /// Sensors of all hardware devices are listed below their measurement type.
    /// </summary>
    SensorType
}
