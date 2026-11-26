// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Text;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Writes sensor readings to CSV files in the layout of the classic app: a line of sensor identifiers, a line of sensor
/// names, then one line per reading with the time first. A new file starts on a new day or when the sensors change.
/// </summary>
public sealed class SensorCsvLogger : IDisposable
{
    private readonly string _directory;
    private StreamWriter _writer;
    private string _columns;
    private DateTime _day;

    /// <summary>
    /// Initializes the logger.
    /// </summary>
    /// <param name="directory">The folder for the log files, created when the first reading is written.</param>
    public SensorCsvLogger(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Gets the file that is currently written, or null before the first reading.
    /// </summary>
    public string CurrentFile { get; private set; }

    /// <summary>
    /// Appends one reading of the given sensors. Nothing is written without sensors, so no empty files appear.
    /// </summary>
    /// <param name="time">The time of the reading.</param>
    /// <param name="sensors">The sensors with identifier, name and value.</param>
    public void Write(DateTime time, IReadOnlyList<(string Identifier, string Name, float? Value)> sensors)
    {
        if (sensors.Count == 0)
            return;

        string columns = string.Join(",", sensors.Select(sensor => sensor.Identifier));

        if (_writer == null || columns != _columns || time.Date != _day)
        {
            Start(time, sensors);
            _columns = columns;
            _day = time.Date;
        }

        StringBuilder line = new(time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        foreach ((string _, string _, float? value) in sensors)
        {
            line.Append(',');
            if (value is { } v && !float.IsNaN(v))
                line.Append(v.ToString("0.###", CultureInfo.InvariantCulture));
        }

        _writer.WriteLine(line.ToString());
    }

    /// <summary>
    /// Closes the current file.
    /// </summary>
    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }

    /// <summary>
    /// Starts a new file with the two header lines.
    /// </summary>
    private void Start(DateTime time, IReadOnlyList<(string Identifier, string Name, float? Value)> sensors)
    {
        _writer?.Dispose();
        Directory.CreateDirectory(_directory);

        CurrentFile = Path.Combine(_directory, "LibreHardwareMonitor-" + time.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".csv");
        _writer = new StreamWriter(CurrentFile, true, new UTF8Encoding(true)) { AutoFlush = true };
        _writer.WriteLine("," + string.Join(",", sensors.Select(sensor => Quote(sensor.Identifier))));
        _writer.WriteLine("Time," + string.Join(",", sensors.Select(sensor => Quote(sensor.Name))));
    }

    /// <summary>
    /// Quotes a CSV field when it contains a separator, a quote or a line break.
    /// </summary>
    private static string Quote(string field)
    {
        field ??= string.Empty;
        return field.IndexOfAny([',', '"', '\n', '\r']) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
