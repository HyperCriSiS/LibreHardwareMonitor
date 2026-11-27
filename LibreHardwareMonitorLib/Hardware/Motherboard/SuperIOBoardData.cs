// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LibreHardwareMonitor.Hardware.Motherboard.Lpc;

namespace LibreHardwareMonitor.Hardware.Motherboard;

/// <summary>
/// Super I/O sensor lists per board, read from an embedded data file instead of code.
/// </summary>
/// <remarks>
/// The data only names sensors, picks chip inputs by index and sets the voltage scaling; it never contains register
/// addresses or anything that writes to the hardware. Boards that need their own read functions, a mutex or GPIO
/// access stay in code in <see cref="SuperIOHardware" />. The SuperIOBoardData tool checks that the file parses and that
/// no board is defined in both places.
/// <para>
/// Format, one field per tab: a line <c>board, chips, manufacturers, Super I/O indices, models</c> (lists separated by commas,
/// <c>*</c> for every manufacturer) starts an entry, followed by lines <c>v, name, index, Ri, Rf, Vf, hidden (0 or 1)</c>, <c>t, name, index</c>,
/// <c>f, name, index</c> and <c>c, name, index</c>. Lines starting with # are comments.
/// </para>
/// </remarks>
internal static class SuperIOBoardData
{
    /// <summary>
    /// The manufacturer field that stands for every manufacturer.
    /// </summary>
    internal const string AllManufacturers = "*";

    private const string ResourceName = "LibreHardwareMonitor.Resources.SuperIOBoards.txt";

    private static readonly Lazy<Dictionary<(Chip, Manufacturer, Model, int), Entry>> Entries = new(Load);

    /// <summary>
    /// Gets or sets whether the data is used. The tool that checks the data turns it off to run the code alone.
    /// </summary>
    internal static bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets the sensor lists of a board, if the data describes it.
    /// </summary>
    internal static bool TryGet(Chip chip, Manufacturer manufacturer, Model model, int superIOIndex, out Entry entry)
    {
        entry = null;
        return Enabled && Entries.Value.TryGetValue((chip, manufacturer, model, superIOIndex), out entry);
    }

    /// <summary>
    /// Parses the data file. A line that does not parse is skipped, so one mistake only affects that line or board.
    /// </summary>
    /// <param name="text">The file content.</param>
    /// <param name="errors">Receives a message for every skipped line, unknown name and board defined twice, if given.</param>
    /// <returns>The entries by chip, manufacturer, model and Super I/O index.</returns>
    internal static Dictionary<(Chip, Manufacturer, Model, int), Entry> Parse(string text, List<string> errors = null)
    {
        Dictionary<(Chip, Manufacturer, Model, int), Entry> entries = new();
        Entry current = null;
        string[] lines = (text ?? string.Empty).Split('\n');

        for (int number = 1; number <= lines.Length; number++)
        {
            string line = lines[number - 1].TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] fields = line.Split('\t');
            bool parsed = fields[0] switch
            {
                "board" => TryParseBoard(fields, entries, out current, message => errors?.Add($"Line {number}: {message}")),
                "v" when current != null && fields.Length == 7 && TryParseInt(fields[2], out int index) && TryParseFloat(fields[3], out float ri) &&
                         TryParseFloat(fields[4], out float rf) && TryParseFloat(fields[5], out float vf) && fields[6] is "0" or "1"
                    => Add(current.Voltages, new Voltage(fields[1], index, ri, rf, vf, fields[6] == "1")),
                "t" when current != null && fields.Length == 3 && TryParseInt(fields[2], out int index) => Add(current.Temperatures, new Temperature(fields[1], index)),
                "f" when current != null && fields.Length == 3 && TryParseInt(fields[2], out int index) => Add(current.Fans, new Fan(fields[1], index)),
                "c" when current != null && fields.Length == 3 && TryParseInt(fields[2], out int index) => Add(current.Controls, new Control(fields[1], index)),
                _ => false
            };

            if (!parsed)
                errors?.Add($"Line {number}: cannot parse \"{line}\".");
        }

        return entries;
    }

    /// <summary>
    /// Formats a number exactly, so it reads back as the same value.
    /// </summary>
    internal static string FormatFloat(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool TryParseBoard(string[] fields, Dictionary<(Chip, Manufacturer, Model, int), Entry> entries, out Entry entry, Action<string> error)
    {
        // The sensor lines of a board with an unknown name still go into an entry, so they are not reported again.
        entry = new Entry();
        if (fields.Length != 5)
            return false;

        List<Manufacturer> manufacturers = new();
        if (fields[2] == AllManufacturers)
        {
            foreach (Manufacturer manufacturer in Enum.GetValues(typeof(Manufacturer)))
            {
                if (!manufacturers.Contains(manufacturer))
                    manufacturers.Add(manufacturer);
            }
        }
        else
        {
            foreach (string manufacturerName in fields[2].Split(','))
            {
                if (Enum.TryParse(manufacturerName, out Manufacturer manufacturer))
                    manufacturers.Add(manufacturer);
                else
                    error($"unknown manufacturer {manufacturerName}.");
            }
        }

        foreach (string chipName in fields[1].Split(','))
        {
            if (!Enum.TryParse(chipName, out Chip chip))
            {
                error($"unknown chip {chipName}.");
                continue;
            }

            foreach (string indexText in fields[3].Split(','))
            {
                if (!TryParseInt(indexText, out int index))
                {
                    error($"invalid Super I/O index {indexText}.");
                    continue;
                }

                foreach (string modelName in fields[4].Split(','))
                {
                    if (!Enum.TryParse(modelName, out Model model))
                    {
                        error($"unknown model {modelName}.");
                        continue;
                    }

                    foreach (Manufacturer manufacturer in manufacturers)
                    {
                        if (entries.ContainsKey((chip, manufacturer, model, index)))
                            error($"{chip} {manufacturer} {model} {index} is defined twice.");
                        else
                            entries[(chip, manufacturer, model, index)] = entry;
                    }
                }
            }
        }

        return true;
    }

    private static bool Add<T>(List<T> list, T item)
    {
        list.Add(item);
        return true;
    }

    private static bool TryParseInt(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Gets the entries read from the embedded data.
    /// </summary>
    internal static Dictionary<(Chip, Manufacturer, Model, int), Entry> Embedded => Entries.Value;

    /// <summary>
    /// Reads the embedded data. A file that cannot be read must not stop the motherboard from loading; its boards then
    /// get the generic sensors of their chip.
    /// </summary>
    private static Dictionary<(Chip, Manufacturer, Model, int), Entry> Load()
    {
        try
        {
            return Parse(ReadResource());
        }
        catch (IOException)
        {
            return new Dictionary<(Chip, Manufacturer, Model, int), Entry>();
        }
    }

    private static string ReadResource()
    {
        using Stream stream = typeof(SuperIOBoardData).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return string.Empty;

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The sensor lists of one board.
    /// </summary>
    internal sealed class Entry
    {
        public List<Voltage> Voltages { get; } = new();

        public List<Temperature> Temperatures { get; } = new();

        public List<Fan> Fans { get; } = new();

        public List<Control> Controls { get; } = new();
    }
}
