// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Stores library settings, for example custom sensor and hardware names, in a JSON file.
/// </summary>
public sealed class JsonSettings : ISettings
{
    private readonly object _lock = new();
    private readonly string _path;
    private readonly Dictionary<string, string> _values;
    private bool _isDirty;

    /// <summary>
    /// Loads the settings from a file.
    /// </summary>
    /// <param name="path">The settings file. A missing or invalid file starts with empty settings.</param>
    public JsonSettings(string path)
    {
        _path = path;
        _values = Load(path);
    }

    /// <inheritdoc />
    public bool Contains(string name)
    {
        lock (_lock)
        {
            return _values.ContainsKey(name);
        }
    }

    /// <inheritdoc />
    public void SetValue(string name, string value)
    {
        // The library stores the recent value history of every sensor on close. It is not needed here
        // and would make the file large.
        if (name.EndsWith("/values", StringComparison.Ordinal))
        {
            return;
        }

        lock (_lock)
        {
            if (!_values.TryGetValue(name, out string current) || current != value)
            {
                _values[name] = value;
                _isDirty = true;
            }
        }
    }

    /// <inheritdoc />
    public string GetValue(string name, string value)
    {
        lock (_lock)
        {
            return _values.TryGetValue(name, out string stored) ? stored : value;
        }
    }

    /// <inheritdoc />
    public void Remove(string name)
    {
        lock (_lock)
        {
            _isDirty |= _values.Remove(name);
        }
    }

    /// <summary>
    /// Writes changed settings to the file. The file is replaced only after the new content is written completely.
    /// </summary>
    public void SaveIfChanged()
    {
        string json;
        lock (_lock)
        {
            if (!_isDirty)
            {
                return;
            }

            json = JsonSerializer.Serialize(_values, JsonSettingsJsonContext.Default.DictionaryStringString);
            _isDirty = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            string temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience, so failing to save must not stop monitoring.
        }
    }

    /// <summary>
    /// Reads the settings file.
    /// </summary>
    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(path), JsonSettingsJsonContext.Default.DictionaryStringString) ?? new();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Invalid or unreadable settings fall back to empty settings.
        }

        return new Dictionary<string, string>();
    }
}

/// <summary>
/// Provides trimming-safe JSON serialization for <see cref="JsonSettings" />.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class JsonSettingsJsonContext : JsonSerializerContext;
