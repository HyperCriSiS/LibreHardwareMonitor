// SPDX-License-Identifier: MPL-2.0

using LibreHardwareMonitor.Avalonia.Monitoring.Models;

namespace LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

/// <summary>
/// Represents a selectable order of the sensors.
/// </summary>
/// <param name="SortOrder">The order of the sensors.</param>
/// <param name="Name">The localized display name.</param>
public sealed record SensorSortOption(SensorSortOrder SortOrder, string Name)
{
    /// <summary>
    /// Returns the display name, which is also used by assistive technologies.
    /// </summary>
    /// <returns>The localized display name.</returns>
    public override string ToString()
    {
        return Name;
    }
}
