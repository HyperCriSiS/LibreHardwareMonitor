// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Avalonia.Mvvm.Formatting;

namespace LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

/// <summary>
/// Shows one pinned sensor on the dashboard with its current value and recent history.
/// </summary>
public sealed class DashboardTileViewModel : ObservableObject
{
    /// <summary>
    /// The number of readings kept for the history line, one per poll.
    /// </summary>
    public const int HistoryLength = 60;

    private readonly Queue<float> _history = new();
    private string _name;
    private string _hardwareName;
    private string _value = string.Empty;
    private IReadOnlyList<float> _historyValues = [];

    /// <summary>
    /// Initializes a tile for a sensor.
    /// </summary>
    /// <param name="identifier">The sensor identifier.</param>
    public DashboardTileViewModel(string identifier)
    {
        Identifier = identifier;
        _name = identifier;
    }

    /// <summary>
    /// Gets the sensor identifier.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Gets the sensor name.
    /// </summary>
    public string Name
    {
        get { return _name; }
        private set { SetProperty(ref _name, value); }
    }

    /// <summary>
    /// Gets the name of the hardware the sensor belongs to.
    /// </summary>
    public string HardwareName
    {
        get { return _hardwareName; }
        private set { SetProperty(ref _hardwareName, value); }
    }

    /// <summary>
    /// Gets the formatted current value.
    /// </summary>
    public string Value
    {
        get { return _value; }
        private set { SetProperty(ref _value, value); }
    }

    /// <summary>
    /// Gets the recent readings, oldest first.
    /// </summary>
    public IReadOnlyList<float> History
    {
        get { return _historyValues; }
        private set { SetProperty(ref _historyValues, value); }
    }

    /// <summary>
    /// Shows the latest reading of the sensor.
    /// </summary>
    /// <param name="sensor">The sensor node, or null if the sensor is currently missing.</param>
    /// <param name="hardwareName">The name of the hardware the sensor belongs to.</param>
    public void Update(SensorTreeNodeSnapshot sensor, string hardwareName)
    {
        if (sensor == null)
        {
            Value = "-";
            return;
        }

        Name = sensor.Name;
        HardwareName = hardwareName;
        Value = SensorValueFormatter.Format(sensor.SensorType, sensor.Value);

        if (sensor.Value is { } value && !float.IsNaN(value))
        {
            _history.Enqueue(value);

            while (_history.Count > HistoryLength)
            {
                _history.Dequeue();
            }

            History = _history.ToArray();
        }
    }
}
