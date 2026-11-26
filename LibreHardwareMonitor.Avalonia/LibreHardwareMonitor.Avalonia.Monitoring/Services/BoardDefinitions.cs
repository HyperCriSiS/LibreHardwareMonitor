// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Reads sensor names for a motherboard from JSON files, so boards can be named without a new release.
/// A file only names or hides sensors; it never contains registers or anything that changes the hardware.
/// </summary>
/// <remarks>
/// Format:
/// <code>
/// {
///   "board": "ROG STRIX X570-E GAMING",
///   "sensors": {
///     "/lpc/nct6798d/0/temperature/3": { "name": "Chipset" },
///     "/lpc/nct6798d/0/voltage/7": { "hidden": true }
///   }
/// }
/// </code>
/// The board name must match the SMBIOS product name exactly (case is ignored). Several files may describe the same
/// board; later files in name order win.
/// </remarks>
public static class BoardDefinitions
{
    /// <summary>
    /// The longest name a file may set, in UTF-16 code units.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// The largest file that is read; board files are a few kilobytes.
    /// </summary>
    public const long MaxFileSize = 1024 * 1024;

    /// <summary>
    /// Loads the definitions for a board from all JSON files in a folder.
    /// </summary>
    /// <param name="directory">The folder with the board files.</param>
    /// <param name="boardName">The SMBIOS product name of the board.</param>
    /// <returns>The names and hidden flags by sensor identifier; empty if nothing matches.</returns>
    public static IReadOnlyDictionary<string, (string Name, bool Hidden)> Load(string directory, string boardName)
    {
        Dictionary<string, (string Name, bool Hidden)> result = new(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(boardName) || !Directory.Exists(directory))
            return result;

        string[] files;

        try
        {
            files = Directory.GetFiles(directory, "*.json");
        }
        catch (Exception)
        {
            // A folder that cannot be listed must not stop monitoring.
            return result;
        }

        foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (new FileInfo(file).Length <= MaxFileSize)
                    Read(File.ReadAllText(file), boardName.Trim(), result);
            }
            catch (Exception)
            {
                // A broken, locked or malformed file (for example with an invalid character) must not stop monitoring;
                // the other files still apply.
            }
        }

        return result;
    }

    /// <summary>
    /// Reads one file and adds its entries if it describes the board.
    /// </summary>
    /// <param name="json">The file content.</param>
    /// <param name="boardName">The SMBIOS product name of the board.</param>
    /// <param name="result">The entries found so far.</param>
    public static void Read(string json, string boardName, Dictionary<string, (string Name, bool Hidden)> result)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("board", out JsonElement board) ||
            board.ValueKind != JsonValueKind.String ||
            !string.Equals(board.GetString()?.Trim(), boardName, StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("sensors", out JsonElement sensors) ||
            sensors.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty sensor in sensors.EnumerateObject())
        {
            if (sensor.Value.ValueKind != JsonValueKind.Object || !sensor.Name.StartsWith('/'))
                continue;

            string name = sensor.Value.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? Clean(nameElement.GetString())
                : null;
            bool hidden = sensor.Value.TryGetProperty("hidden", out JsonElement hiddenElement) && hiddenElement.ValueKind == JsonValueKind.True;

            if (name != null || hidden)
                result[sensor.Name] = (name, hidden);
        }
    }

    /// <summary>
    /// Removes control characters and limits the length, or returns null for an empty name.
    /// </summary>
    private static string Clean(string name)
    {
        // Format characters such as right-to-left overrides could make a name display differently from its text.
        string cleaned = new string((name ?? string.Empty).Where(character => !char.IsControl(character) &&
                                                                               char.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.Format)
                                                           .ToArray()).Trim();

        if (cleaned.Length == 0)
            return null;

        if (cleaned.Length <= MaxNameLength)
            return cleaned;

        // Do not cut a surrogate pair in half.
        int length = char.IsHighSurrogate(cleaned[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength;
        return cleaned.Substring(0, length);
    }
}
