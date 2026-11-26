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
    /// Gets or sets whether the details pane is visible.
    /// </summary>
    public bool IsDetailsVisible { get; set; }

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
