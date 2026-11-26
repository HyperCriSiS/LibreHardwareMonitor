// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

// Generates the Super I/O board data from the board code in SuperIOHardware and checks that the data gives the same
// sensors as the code.
//
//   SuperIOBoardData export <file>   writes the data file
//   SuperIOBoardData verify <file>   fails if the file differs from what the code gives
//
// The code is run for every chip, manufacturer, model and Super I/O index with a stub chip that reads nothing. A board
// goes into the data only if it differs from the generic configuration of its chip and manufacturer, uses the standard
// read functions (no own functions, no mutex, no GPIO), and gives the same sensors as the code for every channel count
// from 0 to MaxChannels. At runtime only channels below the chip's real count become sensors, so the data, written with
// MaxChannels channels, then matches the code for every chip.

using System.Reflection;
using System.Text;
using LibreHardwareMonitor.Hardware.Motherboard;
using LibreHardwareMonitor.Hardware.Motherboard.Lpc;

const int MaxChannels = 64;
const int CheckedChannels = 40;

if (args.Length != 2 || args[0] is not ("export" or "verify"))
{
    Console.Error.WriteLine("Usage: SuperIOBoardData export|verify <file>");
    return 2;
}

SuperIOBoardData.Enabled = false;

MethodInfo method = typeof(SuperIOHardware).GetMethod("GetBoardSpecificConfiguration", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("GetBoardSpecificConfiguration not found.");

string text = Generate(method);

if (args[0] == "export")
{
    File.WriteAllText(args[1], text, new UTF8Encoding(false));
    Console.WriteLine($"Wrote {args[1]}.");
    return 0;
}

string existing = File.Exists(args[1]) ? File.ReadAllText(args[1]).Replace("\r\n", "\n") : string.Empty;
if (existing != text)
{
    Console.Error.WriteLine($"{args[1]} does not match the board code. Run: SuperIOBoardData export {args[1]}");
    return 1;
}

// Golden check through the library itself: for every combination in the embedded data, the board configuration with the
// data must give the same sensors as the code alone, for several channel counts.
int checkedCount = 0;
foreach ((Chip chip, Manufacturer manufacturer, Model model, int index) in SuperIOBoardData.Parse(existing).Keys)
{
    foreach (int channels in new[] { 0, 5, 7, 9, 13, 17, 29, MaxChannels })
    {
        SuperIOBoardData.Enabled = true;
        string data = Run(method, chip, manufacturer, model, index, channels)?.Serialize(channels);
        SuperIOBoardData.Enabled = false;
        string code = Run(method, chip, manufacturer, model, index, channels)?.Serialize(channels);

        if (data == null || data != code)
        {
            Console.Error.WriteLine($"{chip} {manufacturer} {model} {index} with {channels} channels: the data path differs from the code.");
            return 1;
        }
    }

    checkedCount++;
}

Console.WriteLine($"The board data matches the board code; {checkedCount} combinations give the same sensors through the data and the code.");
return 0;

static string Generate(MethodInfo method)
{
    // Content of an entry to the models that share it, per chip, manufacturer and index.
    SortedDictionary<(int Chip, int Manufacturer, int Index), Dictionary<string, List<Model>>> boards = new();
    int skipped = 0;

    foreach (Chip chip in Enum.GetValues<Chip>().Distinct())
    {
        foreach (Manufacturer manufacturer in Enum.GetValues<Manufacturer>().Distinct())
        {
            foreach (int index in new[] { 0, 1 })
            {
                Result baseline = Run(method, chip, manufacturer, Model.Unknown, index, MaxChannels);
                string baselineText = baseline?.Serialize(MaxChannels);

                foreach (Model model in Enum.GetValues<Model>().Distinct())
                {
                    if (model == Model.Unknown)
                        continue;

                    Result result = Run(method, chip, manufacturer, model, index, MaxChannels);
                    if (result == null)
                        continue;

                    string content = result.Serialize(MaxChannels);
                    if (content == baselineText)
                        continue;

                    if (result.IsSpecial || !result.HasPlainNames || !SameForAllChannelCounts(method, chip, manufacturer, model, index, result))
                    {
                        skipped++;
                        continue;
                    }

                    var key = ((int)chip, (int)manufacturer, index);
                    if (!boards.TryGetValue(key, out Dictionary<string, List<Model>> byContent))
                        boards[key] = byContent = new Dictionary<string, List<Model>>();

                    if (!byContent.TryGetValue(content, out List<Model> models))
                        byContent[content] = models = new List<Model>();

                    models.Add(model);
                }
            }
        }
    }

    // The same board code usually serves several chips and both indices, so entries that only differ there share a line.
    var lines = boards.SelectMany(board => board.Value.Select(entry => (board.Key.Chip,
                                                                         board.Key.Manufacturer,
                                                                         board.Key.Index,
                                                                         Models: string.Join(",", entry.Value.OrderBy(model => model)),
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
                      .OrderBy(line => line.Manufacturer)
                      .ThenBy(line => line.Models, StringComparer.Ordinal)
                      .ThenBy(line => line.Chips, StringComparer.Ordinal)
                      .ThenBy(line => line.Indices, StringComparer.Ordinal)
                      .ToList();

    StringBuilder output = new();
    output.Append("# Super I/O sensor lists per board, generated by tools/SuperIOBoardData from SuperIOHardware.cs. Do not edit by hand.\n");
    output.Append("# Boards that need their own read functions, a mutex or GPIO access, or whose lists depend on the channel count, stay in code.\n");

    foreach (var line in lines)
    {
        output.Append($"board\t{line.Chips}\t{(Manufacturer)line.Manufacturer}\t{line.Indices}\t{line.Models}\n");
        output.Append(line.Content);
    }

    Console.WriteLine($"{lines.Count} entries for {boards.Values.Sum(content => content.Count)} combinations, {skipped} combinations kept in code.");
    return output.ToString();
}

static bool SameForAllChannelCounts(MethodInfo method, Chip chip, Manufacturer manufacturer, Model model, int index, Result data)
{
    for (int channels = 0; channels <= CheckedChannels; channels++)
    {
        Result code = Run(method, chip, manufacturer, model, index, channels);
        if (code == null || code.IsSpecial || code.Serialize(channels) != data.Serialize(channels))
            return false;
    }

    return true;
}

static Result Run(MethodInfo method, Chip chip, Manufacturer manufacturer, Model model, int index, int channels)
{
    StubSuperIO stub = new(chip, channels);
    object[] arguments = [stub, manufacturer, model, index, null, null, null, null, null, null, null, null, null, null];

    try
    {
        method.Invoke(null, arguments);
    }
    catch (TargetInvocationException)
    {
        return null;
    }

    bool special = arguments[13] != null || stub.TouchedHardware ||
                   arguments.Skip(8).Take(5).Any(argument => argument is Delegate function && !function.Method.Name.Contains("GetBoardSpecificConfiguration", StringComparison.Ordinal));

    return new Result((IList<Voltage>)arguments[4], (IList<Temperature>)arguments[5], (IList<Fan>)arguments[6], (IList<Control>)arguments[7], special);
}

/// <summary>
/// The sensor lists the board code gives for one combination.
/// </summary>
sealed class Result(IList<Voltage> voltages, IList<Temperature> temperatures, IList<Fan> fans, IList<Control> controls, bool isSpecial)
{
    public bool IsSpecial { get; } = isSpecial;

    /// <summary>
    /// Gets whether all names can be written to the tab separated file.
    /// </summary>
    public bool HasPlainNames => voltages.Select(item => item.Name)
                                         .Concat(temperatures.Select(item => item.Name))
                                         .Concat(fans.Select(item => item.Name))
                                         .Concat(controls.Select(item => item.Name))
                                         .All(name => name != null && name.IndexOfAny(['\t', '\r', '\n']) < 0);

    /// <summary>
    /// Writes the entries a chip with the given channel count turns into sensors, in the order of the code.
    /// </summary>
    public string Serialize(int channels)
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
