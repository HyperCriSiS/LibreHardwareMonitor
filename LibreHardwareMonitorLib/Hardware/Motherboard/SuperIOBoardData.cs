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
/// access stay in code. The file is generated from the code in <see cref="SuperIOHardware" /> by the
/// SuperIOBoardData tool, which also checks that both give the same sensors.
/// <para>
/// Format, one field per tab: a line <c>board, chip, manufacturer, Super I/O index, models separated by commas</c>
/// starts an entry, followed by lines <c>v, name, index, Ri, Rf, Vf, hidden (0 or 1)</c>, <c>t, name, index</c>,
/// <c>f, name, index</c> and <c>c, name, index</c>. Lines starting with # are comments.
/// </para>
/// </remarks>
internal static class SuperIOBoardData
{
    private const string ResourceName = "LibreHardwareMonitor.Resources.SuperIOBoards.txt";

    private static readonly Lazy<Dictionary<(Chip, Manufacturer, Model, int), Entry>> Entries = new(() => Parse(ReadResource()));

    /// <summary>
    /// Gets or sets whether the data is used. The tool that generates and checks the data turns it off to run the code.
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
    /// Parses the data file.
    /// </summary>
    /// <param name="text">The file content.</param>
    /// <returns>The entries by chip, manufacturer, model and Super I/O index.</returns>
    internal static Dictionary<(Chip, Manufacturer, Model, int), Entry> Parse(string text)
    {
        Dictionary<(Chip, Manufacturer, Model, int), Entry> entries = new();
        Entry current = null;

        foreach (string rawLine in (text ?? string.Empty).Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] fields = line.Split('\t');

            switch (fields[0])
            {
                case "board" when fields.Length == 5:
                    current = null;

                    if (!Enum.TryParse(fields[1], out Chip chip) ||
                        !Enum.TryParse(fields[2], out Manufacturer manufacturer) ||
                        !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    {
                        break;
                    }

                    current = new Entry();

                    foreach (string modelName in fields[4].Split(','))
                    {
                        if (Enum.TryParse(modelName, out Model model))
                            entries[(chip, manufacturer, model, index)] = current;
                    }

                    break;

                case "v" when current != null && fields.Length == 7:
                    current.Voltages.Add(new Voltage(fields[1], ParseInt(fields[2]), ParseFloat(fields[3]), ParseFloat(fields[4]), ParseFloat(fields[5]), fields[6] == "1"));
                    break;

                case "t" when current != null && fields.Length == 3:
                    current.Temperatures.Add(new Temperature(fields[1], ParseInt(fields[2])));
                    break;

                case "f" when current != null && fields.Length == 3:
                    current.Fans.Add(new Fan(fields[1], ParseInt(fields[2])));
                    break;

                case "c" when current != null && fields.Length == 3:
                    current.Controls.Add(new Control(fields[1], ParseInt(fields[2])));
                    break;
            }
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

    private static int ParseInt(string text)
    {
        return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static float ParseFloat(string text)
    {
        return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
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
