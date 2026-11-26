// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using LibreHardwareMonitor.Avalonia.Monitoring.Models;

namespace LibreHardwareMonitor.Avalonia.Mvvm.Services;

/// <summary>
/// Holds the view choices that are kept between sessions.
/// </summary>
public sealed class ViewSettings
{
    /// <summary>
    /// Gets or sets the arrangement of the sensor tree.
    /// </summary>
    public SensorGrouping Grouping { get; set; }

    /// <summary>
    /// Gets or sets the order of the sensors.
    /// </summary>
    public SensorSortOrder SortOrder { get; set; }

    /// <summary>
    /// Gets or sets whether hidden sensors are shown.
    /// </summary>
    public bool ShowHidden { get; set; }

    /// <summary>
    /// Gets or sets whether advanced values are shown.
    /// </summary>
    public bool ShowAdvanced { get; set; }

    /// <summary>
    /// Gets or sets whether the details pane is visible.
    /// </summary>
    public bool IsDetailsVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the dashboard is shown instead of the sensor tree.
    /// </summary>
    public bool IsDashboardVisible { get; set; }

    /// <summary>
    /// Gets or sets the identifiers of the sensors pinned to the dashboard, in display order.
    /// </summary>
    public List<string> PinnedSensors { get; set; } = [];

    /// <summary>
    /// Gets or sets whether an icon in the notification area shows the pinned sensors.
    /// </summary>
    public bool IsTrayIconEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether minimizing hides the window to the notification area.
    /// </summary>
    public bool IsMinimizeToTray { get; set; }

    /// <summary>
    /// Gets or sets whether the gadget with the pinned sensors is shown.
    /// </summary>
    public bool IsGadgetVisible { get; set; }

    /// <summary>
    /// Gets or sets the screen position of the gadget, or null to let the system place it.
    /// </summary>
    public int[] GadgetPosition { get; set; }

    /// <summary>
    /// Gets or sets whether the web server with data.json runs.
    /// </summary>
    public bool IsWebServerEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether the web server accepts only requests from this computer.
    /// </summary>
    public bool IsWebServerLocalOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets the TCP port of the web server. 8085 is the port of the classic app.
    /// </summary>
    public int WebServerPort { get; set; } = 8085;

    /// <summary>
    /// Gets or sets the identifiers of the hidden sensors.
    /// </summary>
    public List<string> HiddenSensors { get; set; } = [];

    /// <summary>
    /// Loads settings from a file.
    /// </summary>
    /// <param name="path">The settings file.</param>
    /// <returns>The loaded settings, or defaults if the file is missing or invalid.</returns>
    public static ViewSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(path), ViewSettingsJsonContext.Default.ViewSettings) ?? new ViewSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Invalid or unreadable settings fall back to the defaults.
        }

        return new ViewSettings();
    }

    /// <summary>
    /// Saves the settings to a file, replacing it only after the new content is written completely.
    /// </summary>
    /// <param name="path">The settings file.</param>
    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, ViewSettingsJsonContext.Default.ViewSettings));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience, so failing to save must not stop monitoring.
        }
    }
}

/// <summary>
/// Provides trimming-safe JSON serialization for <see cref="ViewSettings" />.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ViewSettings))]
internal sealed partial class ViewSettingsJsonContext : JsonSerializerContext;
