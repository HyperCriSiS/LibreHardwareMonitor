// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Reads enabled hardware and publishes sensor tree snapshots.
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    /// <summary>
    /// Cancels the background polling loop during shutdown.
    /// </summary>
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>
    /// Owns the hardware devices queried by this monitor.
    /// </summary>
    private readonly Computer _computer;

    /// <summary>
    /// Stores custom names, or null if they are not kept between sessions.
    /// </summary>
    private readonly JsonSettings _settings;

    /// <summary>
    /// Holds renames requested by the user until the polling thread applies them.
    /// </summary>
    private readonly ConcurrentQueue<(string Identifier, string Name)> _renames = new();

    /// <summary>
    /// The folder with the board files, or null to use none.
    /// </summary>
    private readonly string _boardDirectory;

    /// <summary>
    /// Sensor names and hidden flags for this motherboard from the board files.
    /// </summary>
    private IReadOnlyDictionary<string, (string Name, bool Hidden)> _boardDefinitions = new Dictionary<string, (string Name, bool Hidden)>();

    /// <summary>
    /// A pause between two polls longer than this is treated as a resume from sleep.
    /// </summary>
    private static readonly TimeSpan ResumeGap = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Initializes the monitor.
    /// </summary>
    /// <param name="settings">Stores custom names between sessions, or null to keep them only in memory.</param>
    public SensorMonitor(JsonSettings settings = null, string boardDirectory = null)
    {
        _boardDirectory = boardDirectory;
        _settings = settings;
        _computer = settings == null ? new Computer() : new Computer(settings);
        _computer.IsBatteryEnabled = true;
        _computer.IsControllerEnabled = true;
        _computer.IsCpuEnabled = true;
        _computer.IsGpuEnabled = true;
        _computer.IsMemoryEnabled = true;
        _computer.IsMotherboardEnabled = true;
        _computer.IsNetworkEnabled = true;
        _computer.IsPowerMonitorEnabled = true;
        _computer.IsPsuEnabled = true;
        _computer.IsStorageEnabled = true;
    }

    /// <summary>
    /// Keeps properties, because reading them can access the device.
    /// </summary>
    private readonly ConditionalWeakTable<IHardware, CachedProperties> _properties = new();

    /// <summary>
    /// How long properties are kept before they are read again, because some of them change slowly, for example SMART values.
    /// </summary>
    private static readonly TimeSpan PropertiesLifetime = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Keeps the running averages of the sensors.
    /// </summary>
    private readonly ConditionalWeakTable<ISensor, RunningAverage> _averages = new();

    /// <summary>
    /// Tracks the running polling task so monitoring starts only once.
    /// </summary>
    private Task _worker;

    /// <summary>
    /// Occurs when a fresh hardware tree is available.
    /// </summary>
    public event Action<SensorTreeSnapshot> SnapshotAvailable;

    /// <summary>
    /// Occurs when hardware polling fails.
    /// </summary>
    public event Action<string> ErrorOccurred;

    /// <summary>
    /// Starts hardware polling on a background task.
    /// </summary>
    public void Start()
    {
        if (_worker != null)
        {
            return;
        }

        _worker = Task.Run(() => RunAsync(_cancellation.Token));
    }

    /// <summary>
    /// Renames a sensor or hardware device. The name is applied on the polling thread and stored in the settings.
    /// </summary>
    /// <param name="identifier">The identifier of the sensor or hardware.</param>
    /// <param name="name">The new name, or empty to restore the default name.</param>
    public void Rename(string identifier, string name)
    {
        _renames.Enqueue((identifier, name?.Trim() ?? string.Empty));
    }

    /// <summary>
    /// The settings key suffix that marks a name chosen by the user.
    /// </summary>
    private const string UserNameKey = "userName";

    /// <summary>
    /// Loads the board files for the motherboard of this computer.
    /// </summary>
    private void LoadBoardDefinitions()
    {
        _boardDefinitions = _boardDirectory == null
            ? new Dictionary<string, (string Name, bool Hidden)>()
            : BoardDefinitions.Load(_boardDirectory, _computer.SMBios?.Board?.ProductName);
    }

    /// <summary>
    /// Gets whether the user renamed a sensor. Names stored before this was tracked count as the user's choice.
    /// </summary>
    private bool IsRenamedByUser(string identifier)
    {
        if (_settings == null)
            return false;

        string marker = _settings.GetValue(identifier + "/" + UserNameKey, null);
        return marker == "1" || marker == null && _settings.Contains(identifier + "/name");
    }

    /// <summary>
    /// Gets the name and hidden flag of a sensor, using the board files unless the user renamed it.
    /// </summary>
    private (string Name, bool Hidden) GetDisplayName(ISensor sensor)
    {
        string identifier = sensor.Identifier.ToString();

        if (!_boardDefinitions.TryGetValue(identifier, out (string Name, bool Hidden) definition))
            return (sensor.Name, sensor.IsDefaultHidden);

        string name = definition.Name != null && !IsRenamedByUser(identifier) ? definition.Name : sensor.Name;
        return (name, sensor.IsDefaultHidden || definition.Hidden);
    }

    /// <summary>
    /// Applies the requested renames.
    /// </summary>
    private void ApplyRenames()
    {
        while (_renames.TryDequeue(out (string Identifier, string Name) rename))
        {
            // Confirming the name from a board file unchanged in the rename dialog is not a choice of the user, so it
            // keeps following the board file.
            if (_boardDefinitions.TryGetValue(rename.Identifier, out (string Name, bool Hidden) definition) && rename.Name == definition.Name)
                rename = (rename.Identifier, string.Empty);

            foreach (IHardware hardware in _computer.Hardware)
            {
                Rename(hardware, rename.Identifier, rename.Name);
            }

            // Remember whether the user chose the name, so names from board files never replace it.
            _settings?.SetValue(rename.Identifier + "/" + UserNameKey, rename.Name.Length > 0 ? "1" : "0");
        }
    }

    /// <summary>
    /// Renames the hardware, one of its sensors or one of its child devices with the given identifier.
    /// </summary>
    private static void Rename(IHardware hardware, string identifier, string name)
    {
        if (hardware.Identifier.ToString() == identifier)
        {
            hardware.Name = name;
        }

        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Identifier.ToString() == identifier)
            {
                sensor.Name = name;
            }
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            Rename(subHardware, identifier, name);
        }
    }

    /// <summary>
    /// Requests cancellation of the polling loop.
    /// </summary>
    public void Dispose()
    {
        _cancellation.Cancel();
    }

    /// <summary>
    /// Opens the hardware library and publishes snapshots until cancelled.
    /// </summary>
    /// <param name="cancellationToken">The token controlling the polling loop.</param>
    /// <returns>A task that completes when polling stops.</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _computer.Open();
            LoadBoardDefinitions();
            long lastPoll = Environment.TickCount64;

            while (!cancellationToken.IsCancellationRequested)
            {
                // The tick count keeps running during sleep, so a long gap means the system was asleep. Some chips
                // lose their configuration then and report 0 RPM or full duty until they are opened again.
                long now = Environment.TickCount64;
                if (now - lastPoll > ResumeGap.TotalMilliseconds)
                {
                    _computer.Reset();
                    LoadBoardDefinitions();
                }

                lastPoll = now;

                foreach (IHardware hardware in _computer.Hardware)
                {
                    UpdateHardware(hardware);
                }

                ApplyRenames();
                SnapshotAvailable?.Invoke(CreateSnapshot());
                _settings?.SaveIfChanged();

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(exception.Message);
        }
        finally
        {
            _computer.Close();
            _settings?.SaveIfChanged();
        }
    }

    /// <summary>
    /// Refreshes a hardware device and all its child devices.
    /// </summary>
    /// <param name="hardware">The hardware device to refresh.</param>
    private static void UpdateHardware(IHardware hardware)
    {
        hardware.Update();

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            UpdateHardware(subHardware);
        }
    }

    /// <summary>
    /// Creates an immutable tree from the current hardware readings.
    /// </summary>
    /// <returns>The current sensor tree snapshot.</returns>
    private SensorTreeSnapshot CreateSnapshot()
    {
        List<SensorTreeNodeSnapshot> hardware = new();

        foreach (IHardware item in _computer.Hardware)
        {
            hardware.Add(CreateHardwareNode(item));
        }

        SensorTreeNodeSnapshot computer = new("computer",
            Environment.MachineName,
            SensorTreeNodeKind.Computer,
            null,
            null,
            null,
            null,
            null,
            hardware);

        return new SensorTreeSnapshot(DateTime.Now, new[] { computer });
    }

    /// <summary>
    /// Creates a tree node for a hardware device, its sensors and child devices.
    /// </summary>
    /// <param name="hardware">The hardware device to capture.</param>
    /// <returns>The captured hardware tree node.</returns>
    private SensorTreeNodeSnapshot CreateHardwareNode(IHardware hardware)
    {
        List<SensorTreeNodeSnapshot> children = new();

        foreach (IGrouping<SensorType, ISensor> group in hardware.Sensors
                     .OrderBy(sensor => sensor.SensorType)
                     .ThenBy(sensor => sensor.Index)
                     .GroupBy(sensor => sensor.SensorType))
        {
            List<SensorTreeNodeSnapshot> sensors = new();

            foreach (ISensor sensor in group)
            {
                (string name, bool hidden) = GetDisplayName(sensor);
                sensors.Add(new SensorTreeNodeSnapshot(sensor.Identifier.ToString(),
                    name,
                    SensorTreeNodeKind.Sensor,
                    null,
                    sensor.SensorType,
                    sensor.Value,
                    sensor.Min,
                    sensor.Max,
                    Array.Empty<SensorTreeNodeSnapshot>(),
                    null,
                    _averages.GetOrCreateValue(sensor).Add(sensor.Value),
                    hidden));
            }

            children.Add(new SensorTreeNodeSnapshot(hardware.Identifier + "/" + group.Key,
                SensorCategoryName.GetName(group.Key, hardware.HardwareType),
                SensorTreeNodeKind.Category,
                null,
                group.Key,
                null,
                null,
                null,
                sensors));
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            children.Add(CreateHardwareNode(subHardware));
        }

        return new SensorTreeNodeSnapshot(hardware.Identifier.ToString(),
            hardware.Name,
            SensorTreeNodeKind.Hardware,
            hardware.HardwareType,
            null,
            null,
            null,
            null,
            children,
            GetProperties(hardware));
    }

    /// <summary>
    /// Averages the values of a sensor, ignoring missing values.
    /// </summary>
    private sealed class RunningAverage
    {
        private int _count;
        private double _sum;

        /// <summary>
        /// Adds a value and returns the new average.
        /// </summary>
        /// <param name="value">The value to add, if available.</param>
        /// <returns>The average of all values added so far, or null if none was available.</returns>
        public float? Add(float? value)
        {
            if (value.HasValue && !float.IsNaN(value.Value))
            {
                _sum += value.Value;
                _count++;
            }

            return _count > 0 ? (float)(_sum / _count) : null;
        }
    }

    /// <summary>
    /// Gets the properties of a hardware device, reading them again after <see cref="PropertiesLifetime" />.
    /// </summary>
    /// <param name="hardware">The hardware device.</param>
    /// <returns>The properties.</returns>
    private IReadOnlyDictionary<string, string> GetProperties(IHardware hardware)
    {
        DateTime now = DateTime.UtcNow;

        if (!_properties.TryGetValue(hardware, out CachedProperties cached) || now - cached.Read > PropertiesLifetime)
        {
            cached = new CachedProperties(now, ReadProperties(hardware));
            _properties.AddOrUpdate(hardware, cached);
        }

        return cached.Values;
    }

    /// <summary>
    /// Holds properties with the time they were read.
    /// </summary>
    /// <param name="Read">The time the properties were read.</param>
    /// <param name="Values">The properties.</param>
    private sealed record CachedProperties(DateTime Read, IReadOnlyDictionary<string, string> Values);

    /// <summary>
    /// Reads the static properties of a hardware device.
    /// </summary>
    /// <param name="hardware">The hardware device to query.</param>
    /// <returns>The properties, which are empty if the device cannot be read.</returns>
    private static IReadOnlyDictionary<string, string> ReadProperties(IHardware hardware)
    {
        try
        {
            return new Dictionary<string, string>(hardware.Properties);
        }
        catch (Exception)
        {
            // Not retried, because reading could block a busy device on every update.
            return new Dictionary<string, string>();
        }
    }
}
