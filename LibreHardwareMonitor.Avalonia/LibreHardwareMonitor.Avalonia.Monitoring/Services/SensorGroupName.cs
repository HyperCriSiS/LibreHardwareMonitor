// SPDX-License-Identifier: MPL-2.0

using LibreHardwareMonitor.Hardware;
using LanguageResources = LibreHardwareMonitor.Avalonia.Localization.Resources.Resources;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Provides localized names for hardware groups.
/// </summary>
public static class SensorGroupName
{
    /// <summary>
    /// Gets the name of a hardware family.
    /// </summary>
    /// <param name="type">The hardware type of the family.</param>
    /// <returns>The translated family name.</returns>
    public static string GetFamilyName(HardwareType type)
    {
        switch (type)
        {
            case HardwareType.Network: return LanguageResources.FamilyNetwork;
            case HardwareType.Storage: return LanguageResources.FamilyStorage;
            case HardwareType.Memory: return LanguageResources.FamilyMemory;
            default: return type.ToString();
        }
    }

    /// <summary>
    /// Gets the name of a device kind within a family.
    /// </summary>
    /// <param name="kind">The kind reported by the hardware library.</param>
    /// <returns>The translated kind name.</returns>
    public static string GetKindName(string kind)
    {
        switch (kind)
        {
            case "Physical": return LanguageResources.KindPhysical;
            case "Virtual": return LanguageResources.KindVirtual;
            case "Filter": return LanguageResources.KindFilter;
            case "SSD": return LanguageResources.KindSsd;
            case "HDD": return LanguageResources.KindHdd;
            case "Removable": return LanguageResources.KindRemovable;
            default: return LanguageResources.KindOther;
        }
    }
}
