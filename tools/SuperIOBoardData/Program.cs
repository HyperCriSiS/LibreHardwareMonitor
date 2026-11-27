// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

// Checks and formats the Super I/O board data, and records the full board configuration for refactors of the board code.
//
//   SuperIOBoardData format <file>     rewrites the data file in its canonical form
//   SuperIOBoardData verify <file>     fails if the file does not parse, is not canonical, differs from the embedded copy
//                                      or describes a board that is also defined in code
//   SuperIOBoardData snapshot <file>   writes the sensors and read functions of every board, chip and Super I/O index
//   SuperIOBoardData compare <file>    fails if the board configuration differs from a snapshot
//
// The configuration is run for every chip, manufacturer, model and Super I/O index with a stub chip that reads nothing.

using System.Reflection;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware.Motherboard;
using LibreHardwareMonitor.Hardware.Motherboard.Lpc;

const int MaxChannels = 64;
int[] snapshotChannels = [0, 5, 7, 9, 13, 17, 29, MaxChannels];

if (args.Length != 2 || args[0] is not ("format" or "verify" or "snapshot" or "compare"))
{
    Console.Error.WriteLine("Usage: SuperIOBoardData format|verify|snapshot|compare <file>");
    return 2;
}

MethodInfo method = typeof(SuperIOHardware).GetMethod("GetBoardSpecificConfiguration", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("GetBoardSpecificConfiguration not found.");

switch (args[0])
{
    case "format":
    {
        // Lines that do not parse would be dropped, so they must be fixed by hand first.
        if (!TryParse(File.ReadAllText(args[1]), out var entries))
            return 1;

        File.WriteAllText(args[1], Format(entries), new UTF8Encoding(false));
        Console.WriteLine($"Wrote {args[1]}.");
        return 0;
    }

    case "verify":
    {
        string existing = File.ReadAllText(args[1]).Replace("\r\n", "\n");
        if (!TryParse(existing, out var entries))
            return 1;

        if (Format(entries) != existing)
        {
            Console.Error.WriteLine($"{args[1]} is not in canonical form. Run: SuperIOBoardData format {args[1]}");
            return 1;
        }

        // Each board is defined in one place: the code must give the generic configuration for every board in the data,
        // and must run, so the data cannot replace chips with sensors of their own such as IPMI. The data only replaces the
        // sensor lists, so the generic configuration must not need read functions, a mutex or hardware access of its own.
        // A product name is only for boards without a model, so a board is not known by two names.
        int failures = 0;
        var keys = entries.Models.Keys.Select(key => (key.Item1, key.Item2, key.Item3, Name: (string)null, key.Item4))
                          .Concat(entries.Names.Keys.Select(key => (key.Item1, key.Item2, Model.Unknown, Name: key.Item3, key.Item4)));

        foreach ((Chip chip, Manufacturer manufacturer, Model model, string name, int index) in keys)
        {
            string board = $"{chip} {manufacturer} {name ?? model.ToString()} {index}";
            SuperIOBoardData.Enabled = false;
            string generic = Run(method, chip, manufacturer, Model.Unknown, null, index, MaxChannels);
            string code = Run(method, chip, manufacturer, model, null, index, MaxChannels);
            SuperIOBoardData.Enabled = true;
            string data = Run(method, chip, manufacturer, model, name, index, MaxChannels);

            string error = generic.StartsWith("error", StringComparison.Ordinal) || Functions(data) != Functions(generic)
                               ? "this chip cannot be described by the data."
                               : code != generic
                                   ? "the board is defined in the data and in the code."
                                   : name != null && Identification.GetModel(name) != Model.Unknown
                                       ? $"use the model {Identification.GetModel(name)} instead of the product name."
                                       : null;

            if (error != null)
            {
                Console.Error.WriteLine($"{board}: {error}");
                failures++;
            }
        }

        if (failures > 0)
            return 1;

        // The library must load the same boards from its embedded copy, or every board falls back to generic sensors.
        if (Format(SuperIOBoardData.Embedded) != existing)
        {
            Console.Error.WriteLine($"The data embedded in the library differs from {args[1]}.");
            return 1;
        }

        Console.WriteLine("The board data is canonical, matches the embedded copy and no board is defined twice.");
        return 0;
    }

    case "snapshot":
        File.WriteAllText(args[1], Snapshot(method, snapshotChannels), new UTF8Encoding(false));
        Console.WriteLine($"Wrote {args[1]}.");
        return 0;

    default:
    {
        string[] expected = File.ReadAllText(args[1]).Replace("\r\n", "\n").Split('\n');
        string[] actual = Snapshot(method, snapshotChannels).Split('\n');
        if (expected.SequenceEqual(actual))
        {
            Console.WriteLine("The board configuration matches the snapshot.");
            return 0;
        }

        foreach (string line in expected.Except(actual).Take(20))
            Console.Error.WriteLine($"- {line}");

        foreach (string line in actual.Except(expected).Take(20))
            Console.Error.WriteLine($"+ {line}");

        return 1;
    }
}

static bool TryParse(string text, out SuperIOBoardData.Boards entries)
{
    List<string> errors = new();
    entries = SuperIOBoardData.Parse(text, errors);
    foreach (string error in errors)
        Console.Error.WriteLine(error);

    return errors.Count == 0;
}

static string Format(SuperIOBoardData.Boards entries)
{
    // Content of an entry to the boards that share it, per chip, manufacturer and index. Models come first in the order
    // of the enum, then product names.
    SortedDictionary<(int Chip, int Manufacturer, int Index), Dictionary<string, List<(int Order, string Name)>>> boards = new();
    int allManufacturers = Enum.GetValues<Manufacturer>().Distinct().Count();

    var all = entries.Models.Select(pair => (pair.Key.Item1, pair.Key.Item2, pair.Key.Item4, Order: (int)pair.Key.Item3, Name: pair.Key.Item3.ToString(), pair.Value))
                     .Concat(entries.Names.Select(pair => (pair.Key.Item1, pair.Key.Item2, pair.Key.Item4, Order: int.MaxValue, Name: $"\"{pair.Key.Item3}\"", pair.Value)));

    foreach ((Chip chip, Manufacturer manufacturer, int index, int order, string name, SuperIOBoardData.Entry entry) in all)
    {
        string content = Serialize(entry.Voltages, entry.Temperatures, entry.Fans, entry.Controls, MaxChannels);

        var key = ((int)chip, (int)manufacturer, index);
        if (!boards.TryGetValue(key, out var byContent))
            boards[key] = byContent = new Dictionary<string, List<(int, string)>>();

        if (!byContent.TryGetValue(content, out var models))
            byContent[content] = models = new List<(int, string)>();

        models.Add((order, name));
    }

    // The same sensor lists usually serve several chips and both indices, so entries that only differ there share a line.
    var lines = boards.SelectMany(board => board.Value.Select(entry => (board.Key.Chip,
                                                                         board.Key.Manufacturer,
                                                                         board.Key.Index,
                                                                         Models: string.Join(",", entry.Value.OrderBy(model => model.Order).ThenBy(model => model.Name, StringComparer.Ordinal).Select(model => model.Name)),
                                                                         Content: entry.Key)))
                      .GroupBy(line => (line.Manufacturer, line.Models, line.Content))
                      .SelectMany(group => group.GroupBy(line => line.Chip)
                                                .Select(byChip => (Chip: byChip.Key, Indices: string.Join(",", byChip.Select(line => line.Index).OrderBy(index => index))))
                                                .GroupBy(chip => chip.Indices)
                                                .Select(byIndices => (Chips: string.Join(",", byIndices.Select(chip => (Chip)chip.Chip).OrderBy(chip => chip)),
                                                                      Indices: byIndices.Key,
                                                                      group.Key.Manufacturer,
                                                                      group.Key.Models,
                                                                      group.Key.Content)))
                      // Boards that are the same for several manufacturers share a line too, with * for all of them.
                      .GroupBy(line => (line.Chips, line.Indices, line.Models, line.Content))
                      .Select(group => (Manufacturers: group.Count() == allManufacturers
                                                           ? SuperIOBoardData.AllManufacturers
                                                           : string.Join(",", group.Select(line => (Manufacturer)line.Manufacturer).OrderBy(manufacturer => manufacturer)),
                                        group.Key.Chips,
                                        group.Key.Indices,
                                        group.Key.Models,
                                        group.Key.Content,
                                        First: group.Min(line => line.Manufacturer)))
                      .OrderBy(line => line.Manufacturers == SuperIOBoardData.AllManufacturers ? -1 : line.First)
                      .ThenBy(line => line.Manufacturers, StringComparer.Ordinal)
                      .ThenBy(line => line.Models, StringComparer.Ordinal)
                      .ThenBy(line => line.Chips, StringComparer.Ordinal)
                      .ThenBy(line => line.Indices, StringComparer.Ordinal)
                      .ToList();

    StringBuilder output = new();
    output.Append("# Super I/O sensor lists per board; the format is described in SuperIOBoardData.cs. Check with tools/SuperIOBoardData verify, sort with format.\n");
    output.Append("# Boards that need their own read functions, a mutex or GPIO access, or whose lists depend on the channel count, stay in SuperIOHardware.cs.\n");

    foreach (var line in lines)
    {
        output.Append($"board\t{line.Chips}\t{line.Manufacturers}\t{line.Indices}\t{line.Models}\n");
        output.Append(line.Content);
    }

    return output.ToString();
}

static string Snapshot(MethodInfo method, int[] channelCounts)
{
    // Every combination that differs from the generic configuration of its chip, manufacturer and index, plus the generic
    // configurations themselves, so a board that falls back to the generic one shows up as a removed line.
    StringBuilder output = new();

    foreach (Chip chip in Enum.GetValues<Chip>().Distinct())
    {
        foreach (Manufacturer manufacturer in Enum.GetValues<Manufacturer>().Distinct())
        {
            foreach (int index in new[] { 0, 1 })
            {
                string generic = Run(method, chip, manufacturer, Model.Unknown, null, index, MaxChannels);

                foreach (Model model in Enum.GetValues<Model>().Distinct())
                {
                    if (model != Model.Unknown && Run(method, chip, manufacturer, model, null, index, MaxChannels) == generic)
                        continue;

                    output.Append($"{chip} {manufacturer} {model} {index}");
                    foreach (int channels in channelCounts)
                        output.Append(" | ").Append(Run(method, chip, manufacturer, model, null, index, channels).Replace('\n', ';').Replace('\t', ' '));

                    output.Append('\n');
                }
            }
        }
    }

    return output.ToString();
}

/// <summary>
/// Gets the read functions, mutex and hardware access of a run, without its sensors.
/// </summary>
static string Functions(string run)
{
    return string.Join("\n", run.Split('\n').Where(line => line.Length < 2 || line[1] != '\t'));
}

/// <summary>
/// Runs the board configuration and writes its sensors, read functions and mutex, or "error" if it throws.
/// </summary>
static string Run(MethodInfo method, Chip chip, Manufacturer manufacturer, Model model, string productName, int index, int channels)
{
    StubSuperIO stub = new(chip, channels);
    object[] arguments = [stub, manufacturer, model, productName, index, null, null, null, null, null, null, null, null, null, null];

    try
    {
        method.Invoke(null, arguments);
    }
    catch (TargetInvocationException exception)
    {
        return $"error {exception.InnerException?.GetType().Name}";
    }

    StringBuilder text = new(Serialize((IList<Voltage>)arguments[5], (IList<Temperature>)arguments[6], (IList<Fan>)arguments[7], (IList<Control>)arguments[8], channels));

    foreach (Delegate function in arguments.Skip(9).Take(5).Cast<Delegate>())
        text.Append($"fn\t{function.Method.DeclaringType?.Name}.{function.Method.Name}\n");

    if (arguments[14] is Mutex)
        text.Append("mutex\n");

    if (stub.TouchedHardware)
        text.Append("hardware\n");

    return text.ToString();
}

/// <summary>
/// Writes the entries a chip with the given channel count turns into sensors, in the order of the lists.
/// </summary>
static string Serialize(IList<Voltage> voltages, IList<Temperature> temperatures, IList<Fan> fans, IList<Control> controls, int channels)
{
    StringBuilder text = new();

    foreach (Voltage item in voltages.Where(item => item.Index < channels))
        text.Append($"v\t{item.Name}\t{item.Index}\t{SuperIOBoardData.FormatFloat(item.Ri)}\t{SuperIOBoardData.FormatFloat(item.Rf)}\t{SuperIOBoardData.FormatFloat(item.Vf)}\t{(item.Hidden ? 1 : 0)}\n");

    foreach (Temperature item in temperatures.Where(item => item.Index < channels))
        text.Append($"t\t{item.Name}\t{item.Index}\n");

    foreach (Fan item in fans.Where(item => item.Index < channels))
        text.Append($"f\t{item.Name}\t{item.Index}\n");

    foreach (Control item in controls.Where(item => item.Index < channels))
        text.Append($"c\t{item.Name}\t{item.Index}\n");

    return text.ToString();
}

/// <summary>
/// A chip that reads nothing and records whether the board code touched the hardware.
/// </summary>
sealed class StubSuperIO(Chip chip, int channels) : ISuperIO
{
    public bool TouchedHardware { get; private set; }

    public Chip Chip { get; } = chip;

    public float?[] Controls { get; } = new float?[channels];

    public float?[] Fans { get; } = new float?[channels];

    public float?[] Temperatures { get; } = new float?[channels];

    public float?[] Voltages { get; } = new float?[channels];

    public void SetControl(int index, byte? value) => TouchedHardware = true;

    public byte? ReadGpio(int index)
    {
        TouchedHardware = true;
        return null;
    }

    public void WriteGpio(int index, byte value) => TouchedHardware = true;

    public string GetReport() => string.Empty;

    public void Update() => TouchedHardware = true;

    public void Close() => TouchedHardware = true;
}
