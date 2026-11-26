// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Avalonia.Localization.Services;
using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Avalonia.Monitoring.Services;
using LibreHardwareMonitor.Avalonia.Mvvm.Services;
using LanguageResources = LibreHardwareMonitor.Avalonia.Localization.Resources.Resources;

namespace LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

/// <summary>
/// Holds the current sensor tree and monitoring status for the main window.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Keeps one tree per grouping, so expanded nodes are preserved when switching.
    /// </summary>
    private readonly Dictionary<SensorGrouping, ObservableCollection<SensorTreeNodeViewModel>> _trees = new();

    private SensorTreeSnapshot _snapshot;
    private SensorGroupingOption _selectedGrouping;
    private SensorTreeNodeViewModel _selectedNode;
    private IReadOnlyList<DetailViewModel> _details = [];
    private bool _isDetailsVisible;
    private readonly Dictionary<string, Queue<float>> _histories = new();
    private IReadOnlyList<float> _selectedHistory = [];
    private string _selectedHistoryRange = string.Empty;
    private bool _isWebServerEnabled;
    private bool _isLoggingEnabled;
    private bool _isGadgetVisible;
    private bool _isTrayIconEnabled = true;
    private bool _isMinimizeToTray;
    private bool _isWebServerLocalOnly = true;
    private int _webServerPort = 8085;
    private string _webServerStatus = string.Empty;
    private bool _isDashboardVisible;
    private string _filterText = string.Empty;
    private readonly HashSet<string> _hiddenSensors = new();
    private bool _showHidden;
    private bool _showAdvanced;
    private readonly Dictionary<string, SensorSortOrder> _groupSortOrders = new();
    private SensorSortOption _selectedSortOrder;
    private string _status = LanguageResources.StatusSearching;
    private string _hardwareWarning;

    /// <summary>
    /// Initializes the main window with sensors grouped by hardware.
    /// </summary>
    public MainWindowViewModel()
    {
        foreach (SensorGroupingOption option in Groupings)
        {
            _trees.Add(option.Grouping, new ObservableCollection<SensorTreeNodeViewModel>());
        }

        _selectedGrouping = Groupings[0];
        _selectedSortOrder = SortOrders[0];
    }

    /// <summary>
    /// Occurs when the user renames a sensor or hardware. The arguments are the identifier and the new name,
    /// which is empty to restore the default name.
    /// </summary>
    public event Action<string, string> RenameRequested;

    /// <summary>
    /// Gets or sets the function that creates the anonymized hardware report, or null if monitoring is not running.
    /// </summary>
    public Func<Task<string>> ReportProvider { get; set; }

    /// <summary>
    /// Gets whether a node can be renamed.
    /// </summary>
    /// <param name="node">The node to check.</param>
    /// <returns>True for sensors and hardware.</returns>
    public static bool CanRename(SensorTreeNodeViewModel node)
    {
        return node?.Kind is SensorTreeNodeKind.Sensor or SensorTreeNodeKind.Hardware;
    }

    /// <summary>
    /// Renames a sensor or hardware.
    /// </summary>
    /// <param name="node">The node to rename.</param>
    /// <param name="name">The new name, or empty to restore the default name.</param>
    public void Rename(SensorTreeNodeViewModel node, string name)
    {
        if (CanRename(node))
        {
            RenameRequested?.Invoke(GetSourceIdentifier(node), name ?? string.Empty);
        }
    }

    /// <summary>
    /// Gets the identifier that the hardware library uses for a node, also when the node is a copy in another grouping.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The library identifier.</returns>
    private static string GetSourceIdentifier(SensorTreeNodeViewModel node)
    {
        // Hardware grouped by sensor type is copied as "type/<hardware>/<sensor type>".
        if (node.Kind == SensorTreeNodeKind.Hardware && node.Identifier.StartsWith("type/", StringComparison.Ordinal))
        {
            string identifier = node.Identifier.Substring("type/".Length);
            return identifier.Substring(0, identifier.LastIndexOf('/'));
        }

        return node.Identifier;
    }

    /// <summary>
    /// Gets the sensor mapping assistant.
    /// </summary>
    public SensorMappingViewModel Mapping { get; } = new();

    /// <summary>
    /// Occurs when a view choice that is kept between sessions changes.
    /// </summary>
    public event Action SettingsChanged;

    /// <summary>
    /// Restores view choices from a previous session.
    /// </summary>
    /// <param name="settings">The stored view choices.</param>
    public void ApplySettings(ViewSettings settings)
    {
        _hiddenSensors.Clear();
        _hiddenSensors.UnionWith(settings.HiddenSensors ?? []);

        SelectedGrouping = Groupings.FirstOrDefault(option => option.Grouping == settings.Grouping) ?? Groupings[0];
        SelectedSortOrder = SortOrders.FirstOrDefault(option => option.SortOrder == settings.SortOrder) ?? SortOrders[0];
        ShowHidden = settings.ShowHidden;
        ShowAdvanced = settings.ShowAdvanced;
        IsDetailsVisible = settings.IsDetailsVisible;
        DetailsWidth = settings.DetailsWidth is > 0 and < 10000 ? settings.DetailsWidth : MainWindowDefaults.DetailsWidth;
        WebServerPort = settings.WebServerPort is > 0 and < 65536 ? settings.WebServerPort : 8085;
        IsWebServerLocalOnly = settings.IsWebServerLocalOnly;
        IsWebServerEnabled = settings.IsWebServerEnabled;
        IsLoggingEnabled = settings.IsLoggingEnabled;
        GadgetPosition = settings.GadgetPosition is [int x, int y] ? (x, y) : null;
        IsGadgetVisible = settings.IsGadgetVisible;
        UseFahrenheit = settings.UseFahrenheit;
        IsTrayIconEnabled = settings.IsTrayIconEnabled;
        IsMinimizeToTray = settings.IsMinimizeToTray;
        IsDashboardVisible = settings.IsDashboardVisible;
        Tiles.Clear();

        foreach (string identifier in (settings.PinnedSensors ?? []).Distinct())
        {
            Tiles.Add(new DashboardTileViewModel(identifier));
        }

        OnPropertyChanged(nameof(HasTiles));
        UpdateTree();
    }

    /// <summary>
    /// Gets the view choices to keep for the next session.
    /// </summary>
    /// <returns>The current view choices.</returns>
    public ViewSettings GetSettings()
    {
        return new ViewSettings
        {
            Grouping = SelectedGrouping.Grouping,
            SortOrder = SelectedSortOrder.SortOrder,
            ShowHidden = ShowHidden,
            ShowAdvanced = ShowAdvanced,
            IsDetailsVisible = IsDetailsVisible,
            DetailsWidth = DetailsWidth,
            IsWebServerEnabled = IsWebServerEnabled,
            IsLoggingEnabled = IsLoggingEnabled,
            IsGadgetVisible = IsGadgetVisible,
            GadgetPosition = GadgetPosition is { } position ? [position.X, position.Y] : null,
            UseFahrenheit = UseFahrenheit,
            IsTrayIconEnabled = IsTrayIconEnabled,
            IsMinimizeToTray = IsMinimizeToTray,
            IsWebServerLocalOnly = IsWebServerLocalOnly,
            WebServerPort = WebServerPort,
            IsDashboardVisible = IsDashboardVisible,
            PinnedSensors = Tiles.Select(tile => tile.Identifier).ToList(),
            HiddenSensors = _hiddenSensors.Order(StringComparer.Ordinal).ToList()
        };
    }

    /// <summary>
    /// Gets the available arrangements of the sensor tree.
    /// </summary>
    public IReadOnlyList<SensorGroupingOption> Groupings { get; } =
    [
        new(SensorGrouping.Hardware, LanguageResources.GroupingHardware),
        new(SensorGrouping.SensorType, LanguageResources.GroupingSensorType)
    ];

    /// <summary>
    /// Gets or sets the current arrangement of the sensor tree.
    /// </summary>
    public SensorGroupingOption SelectedGrouping
    {
        get { return _selectedGrouping; }
        set
        {
            if (value != null && SetProperty(ref _selectedGrouping, value))
            {
                SelectedNode = null;
                UpdateTree();
                OnPropertyChanged(nameof(Hardware));
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets the available sensor orders.
    /// </summary>
    public IReadOnlyList<SensorSortOption> SortOrders { get; } =
    [
        new(SensorSortOrder.Default, LanguageResources.SortDefault),
        new(SensorSortOrder.Name, LanguageResources.SortName),
        new(SensorSortOrder.ValueDescending, LanguageResources.SortValue),
        new(SensorSortOrder.ValueAscending, LanguageResources.SortValueAscending),
        new(SensorSortOrder.MaxDescending, LanguageResources.SortMax),
        new(SensorSortOrder.AverageDescending, LanguageResources.SortAverage)
    ];

    /// <summary>
    /// Changes the order of the sensors below one category: descending, ascending, then back to the general order.
    /// </summary>
    /// <param name="node">The category.</param>
    /// <param name="column">The column: "Value", "Min", "Max" or "Average".</param>
    public void ToggleGroupSort(SensorTreeNodeViewModel node, string column)
    {
        if (node?.Kind != SensorTreeNodeKind.Category || !Enum.TryParse(column + "Descending", out SensorSortOrder descending))
        {
            return;
        }

        SensorSortOrder ascending = descending + 1;

        if (!_groupSortOrders.TryGetValue(node.Identifier, out SensorSortOrder current) || (current != descending && current != ascending))
        {
            _groupSortOrders[node.Identifier] = descending;
        }
        else if (current == descending)
        {
            _groupSortOrders[node.Identifier] = ascending;
        }
        else
        {
            _groupSortOrders.Remove(node.Identifier);
        }

        UpdateTree();
    }

    /// <summary>
    /// Shows the column captions in the category rows, with an arrow at the column a category is sorted by.
    /// </summary>
    /// <param name="nodes">The nodes to update.</param>
    private void SetCaptions(IEnumerable<SensorTreeNodeViewModel> nodes)
    {
        foreach (SensorTreeNodeViewModel node in nodes)
        {
            if (node.IsCategory)
            {
                _groupSortOrders.TryGetValue(node.Identifier, out SensorSortOrder order);
                node.SetCaptions(Caption(LanguageResources.ColumnValue, order, SensorSortOrder.ValueDescending),
                                 Caption(LanguageResources.ColumnMinimum, order, SensorSortOrder.MinDescending),
                                 Caption(LanguageResources.ColumnMaximum, order, SensorSortOrder.MaxDescending),
                                 Caption(LanguageResources.ColumnAverage, order, SensorSortOrder.AverageDescending));
            }

            SetCaptions(node.Children);
        }

        static string Caption(string caption, SensorSortOrder order, SensorSortOrder descending)
        {
            return order == descending ? caption + " \u25BC" : order == descending + 1 ? caption + " \u25B2" : caption;
        }
    }

    /// <summary>
    /// Gets or sets the order of the sensors within their parent.
    /// </summary>
    public SensorSortOption SelectedSortOrder
    {
        get { return _selectedSortOrder; }
        set
        {
            if (value != null && SetProperty(ref _selectedSortOrder, value))
            {
                UpdateTree();
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the text that hardware or sensor names must contain.
    /// </summary>
    public string FilterText
    {
        get { return _filterText; }
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                UpdateTree();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether advanced values are shown: sensors that are hidden by default and advanced details.
    /// </summary>
    public bool ShowAdvanced
    {
        get { return _showAdvanced; }
        set
        {
            if (SetProperty(ref _showAdvanced, value))
            {
                UpdateTree();
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Property name prefixes of details that are shown only with the advanced values.
    /// </summary>
    private static readonly string[] AdvancedPropertyPrefixes = ["SMART ", "PCIe ", "Throttle ", "Advanced ", "RAM "];

    /// <summary>
    /// Gets or sets whether hidden sensors are shown.
    /// </summary>
    public bool ShowHidden
    {
        get { return _showHidden; }
        set
        {
            if (SetProperty(ref _showHidden, value))
            {
                UpdateTree();
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Renames a sensor by its library identifier, for example from the sensor mapping.
    /// </summary>
    /// <param name="identifier">The sensor identifier.</param>
    /// <param name="name">The new name, or empty to restore the default name.</param>
    public void RenameSensor(string identifier, string name)
    {
        RenameRequested?.Invoke(identifier, name ?? string.Empty);
    }

    /// <summary>
    /// Hides a sensor by its identifier, for example an unconnected input found by the sensor mapping.
    /// </summary>
    /// <param name="identifier">The sensor identifier.</param>
    public void HideSensor(string identifier)
    {
        if (_hiddenSensors.Add(identifier))
        {
            UpdateTree();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Hides or shows the given sensors, including all sensors below the given hardware and categories.
    /// </summary>
    /// <param name="nodes">The selected nodes.</param>
    /// <param name="isHidden">True to hide the sensors, false to show them again.</param>
    public void SetHidden(IEnumerable<SensorTreeNodeViewModel> nodes, bool isHidden)
    {
        foreach (string identifier in nodes.SelectMany(GetSensorIdentifiers).ToList())
        {
            if (isHidden)
            {
                _hiddenSensors.Add(identifier);
            }
            else
            {
                _hiddenSensors.Remove(identifier);
            }
        }

        UpdateTree();
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Gets the identifiers of a sensor or of all sensors below a node.
    /// </summary>
    /// <param name="node">The node to search.</param>
    /// <returns>The sensor identifiers.</returns>
    private static IEnumerable<string> GetSensorIdentifiers(SensorTreeNodeViewModel node)
    {
        return node.Kind == SensorTreeNodeKind.Sensor
            ? [node.Identifier]
            : node.Children.SelectMany(GetSensorIdentifiers);
    }

    /// <summary>
    /// Marks the hidden sensors that are shown.
    /// </summary>
    /// <param name="nodes">The nodes to mark.</param>
    private void MarkHidden(IEnumerable<SensorTreeNodeViewModel> nodes)
    {
        foreach (SensorTreeNodeViewModel node in nodes)
        {
            node.IsHidden = node.Kind == SensorTreeNodeKind.Sensor && _hiddenSensors.Contains(node.Identifier);
            MarkHidden(node.Children);
        }
    }

    /// <summary>
    /// Gets the root nodes of the sensor tree.
    /// </summary>
    public ObservableCollection<SensorTreeNodeViewModel> Hardware
    {
        get { return _trees[SelectedGrouping.Grouping]; }
    }

    /// <summary>
    /// Gets or sets the selected tree node.
    /// </summary>
    public SensorTreeNodeViewModel SelectedNode
    {
        get { return _selectedNode; }
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                UpdateDetails();
                UpdateSelectedHistory();
            }
        }
    }

    /// <summary>
    /// The number of readings kept per sensor for the history in the details pane, one per poll.
    /// </summary>
    public const int SensorHistoryLength = 300;

    /// <summary>
    /// Gets the recent readings of the selected sensor, oldest first, or an empty list if no sensor is selected.
    /// </summary>
    public IReadOnlyList<float> SelectedHistory
    {
        get { return _selectedHistory; }
        private set { SetProperty(ref _selectedHistory, value); }
    }

    /// <summary>
    /// Gets the lowest and highest reading of the selected history as text.
    /// </summary>
    public string SelectedHistoryRange
    {
        get { return _selectedHistoryRange; }
        private set { SetProperty(ref _selectedHistoryRange, value); }
    }

    /// <summary>
    /// Adds the readings of a snapshot to the history of each sensor.
    /// </summary>
    private void RecordHistories(SensorTreeSnapshot snapshot)
    {
        void Record(SensorTreeNodeSnapshot node)
        {
            if (node.Kind == SensorTreeNodeKind.Sensor && node.Value is { } value && !float.IsNaN(value))
            {
                if (!_histories.TryGetValue(node.Identifier, out Queue<float> history))
                {
                    history = new Queue<float>();
                    _histories.Add(node.Identifier, history);
                }

                history.Enqueue(value);

                if (history.Count > SensorHistoryLength)
                    history.Dequeue();
            }

            foreach (SensorTreeNodeSnapshot child in node.Children)
                Record(child);
        }

        foreach (SensorTreeNodeSnapshot root in snapshot.Roots)
            Record(root);
    }

    /// <summary>
    /// Shows the history of the selected sensor.
    /// </summary>
    private void UpdateSelectedHistory()
    {
        if (SelectedNode is { Kind: SensorTreeNodeKind.Sensor } node && _histories.TryGetValue(node.Identifier, out Queue<float> history) && history.Count > 1)
        {
            float[] values = history.ToArray();
            SelectedHistory = values;
            SelectedHistoryRange = string.Format(CultureInfo.CurrentCulture,
                LanguageResources.HistoryRangeFormat,
                Formatting.SensorValueFormatter.Format(node.Snapshot.SensorType, values.Min()),
                Formatting.SensorValueFormatter.Format(node.Snapshot.SensorType, values.Max()));
        }
        else
        {
            SelectedHistory = [];
            SelectedHistoryRange = string.Empty;
        }
    }

    /// <summary>
    /// Gets the details of the selected tree node and the hardware that provides it.
    /// </summary>
    public IReadOnlyList<DetailViewModel> Details
    {
        get { return _details; }
        private set { SetProperty(ref _details, value); }
    }

    /// <summary>
    /// Gets or sets whether the details pane is visible.
    /// </summary>
    public bool IsDetailsVisible
    {
        get { return _isDetailsVisible; }
        set
        {
            if (SetProperty(ref _isDetailsVisible, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the dashboard is shown instead of the sensor tree.
    /// </summary>
    public bool IsDashboardVisible
    {
        get { return _isDashboardVisible; }
        set
        {
            if (SetProperty(ref _isDashboardVisible, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets the sensors pinned to the dashboard.
    /// </summary>
    public ObservableCollection<DashboardTileViewModel> Tiles { get; } = new();

    /// <summary>
    /// Gets whether any sensor is pinned to the dashboard.
    /// </summary>
    public bool HasTiles => Tiles.Count > 0;

    /// <summary>
    /// Pins the given sensors to the dashboard or removes them, including all sensors below hardware and categories.
    /// </summary>
    /// <param name="nodes">The selected nodes.</param>
    /// <param name="isPinned">True to pin the sensors, false to remove them.</param>
    public void SetPinned(IEnumerable<SensorTreeNodeViewModel> nodes, bool isPinned)
    {
        SetPinned(nodes.SelectMany(GetSensorIdentifiers), isPinned);
    }

    /// <summary>
    /// Pins sensors to the dashboard or removes them by identifier.
    /// </summary>
    /// <param name="identifiers">The sensor identifiers.</param>
    /// <param name="isPinned">True to pin the sensors, false to remove them.</param>
    public void SetPinned(IEnumerable<string> identifiers, bool isPinned)
    {
        foreach (string identifier in identifiers.ToList())
        {
            DashboardTileViewModel tile = Tiles.FirstOrDefault(item => item.Identifier == identifier);

            if (isPinned && tile == null)
            {
                Tiles.Add(new DashboardTileViewModel(identifier));
            }
            else if (!isPinned && tile != null)
            {
                Tiles.Remove(tile);
            }
        }

        OnPropertyChanged(nameof(HasTiles));
        UpdateDashboard();
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Shows the latest readings on the dashboard tiles.
    /// </summary>
    private void UpdateDashboard()
    {
        if (_snapshot == null || Tiles.Count == 0)
            return;

        Dictionary<string, (SensorTreeNodeSnapshot Sensor, string Hardware)> sensors = new();

        void Collect(SensorTreeNodeSnapshot node, string hardware)
        {
            if (node.Kind == SensorTreeNodeKind.Hardware)
                hardware = node.Name;

            if (node.Kind == SensorTreeNodeKind.Sensor)
                sensors.TryAdd(node.Identifier, (node, hardware));

            foreach (SensorTreeNodeSnapshot child in node.Children)
                Collect(child, hardware);
        }

        foreach (SensorTreeNodeSnapshot root in _snapshot.Roots)
            Collect(root, null);

        foreach (DashboardTileViewModel tile in Tiles)
        {
            if (sensors.TryGetValue(tile.Identifier, out (SensorTreeNodeSnapshot Sensor, string Hardware) found))
                tile.Update(found.Sensor, found.Hardware);
            else
                tile.Update(null, null);
        }
    }

    /// <summary>
    /// Gets or sets whether an icon in the notification area shows the pinned sensors.
    /// </summary>
    public bool IsTrayIconEnabled
    {
        get { return _isTrayIconEnabled; }
        set
        {
            if (SetProperty(ref _isTrayIconEnabled, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether minimizing hides the window to the notification area.
    /// </summary>
    public bool IsMinimizeToTray
    {
        get { return _isMinimizeToTray; }
        set
        {
            if (SetProperty(ref _isMinimizeToTray, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets a short text with the values of the pinned sensors, for the tooltip of the notification area icon.
    /// </summary>
    /// <param name="maxLength">The maximum length; Windows cuts tooltips of notification icons at 127 characters.</param>
    /// <returns>The text, or the application name if nothing is pinned.</returns>
    public string GetTrayText(int maxLength = 127)
    {
        string text = Tiles.Count == 0
            ? LanguageResources.ApplicationTitle
            : string.Join("\n", Tiles.Select(tile => tile.Name + ": " + tile.Value));

        return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "\u2026";
    }

    /// <summary>
    /// Gets or sets whether temperatures are shown in degrees Fahrenheit. The web server keeps Celsius like the raw values.
    /// </summary>
    public bool UseFahrenheit
    {
        get { return Formatting.SensorValueFormatter.UseFahrenheit; }
        set
        {
            if (Formatting.SensorValueFormatter.UseFahrenheit != value)
            {
                Formatting.SensorValueFormatter.UseFahrenheit = value;
                OnPropertyChanged();
                UpdateTree();
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the gadget with the pinned sensors is shown.
    /// </summary>
    public bool IsGadgetVisible
    {
        get { return _isGadgetVisible; }
        set
        {
            if (SetProperty(ref _isGadgetVisible, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the screen position of the gadget, or null to let the system place it. Stored with the next settings change.
    /// </summary>
    public (int X, int Y)? GadgetPosition { get; set; }

    /// <summary>
    /// Gets or sets whether the pinned sensors are written to CSV files.
    /// </summary>
    public bool IsLoggingEnabled
    {
        get { return _isLoggingEnabled; }
        set
        {
            if (SetProperty(ref _isLoggingEnabled, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets the latest readings of the pinned sensors in dashboard order, for the CSV log.
    /// </summary>
    /// <returns>The identifier, name and value of each pinned sensor that is present.</returns>
    public IReadOnlyList<(string Identifier, string Name, float? Value)> GetPinnedReadings()
    {
        if (_snapshot == null || Tiles.Count == 0)
            return [];

        Dictionary<string, SensorTreeNodeSnapshot> sensors = new();

        void Collect(SensorTreeNodeSnapshot node)
        {
            if (node.Kind == SensorTreeNodeKind.Sensor)
                sensors.TryAdd(node.Identifier, node);

            foreach (SensorTreeNodeSnapshot child in node.Children)
                Collect(child);
        }

        foreach (SensorTreeNodeSnapshot root in _snapshot.Roots)
            Collect(root);

        return Tiles.Where(tile => sensors.ContainsKey(tile.Identifier))
                    .Select(tile => (tile.Identifier, sensors[tile.Identifier].Name, sensors[tile.Identifier].Value))
                    .ToList();
    }

    /// <summary>
    /// Gets or sets whether the web server with data.json runs.
    /// </summary>
    public bool IsWebServerEnabled
    {
        get { return _isWebServerEnabled; }
        set
        {
            if (SetProperty(ref _isWebServerEnabled, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the web server accepts only requests from this computer.
    /// </summary>
    public bool IsWebServerLocalOnly
    {
        get { return _isWebServerLocalOnly; }
        set
        {
            if (SetProperty(ref _isWebServerLocalOnly, value))
            {
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets the TCP port of the web server.
    /// </summary>
    public int WebServerPort
    {
        get { return _webServerPort; }
        private set { SetProperty(ref _webServerPort, value); }
    }

    /// <summary>
    /// Gets or sets the address of data.json or why the web server could not start.
    /// </summary>
    public string WebServerStatus
    {
        get { return _webServerStatus; }
        set { SetProperty(ref _webServerStatus, value); }
    }

    /// <summary>
    /// Gets or sets the width of the details pane beside the tree. The window stores it when the user finishes dragging.
    /// </summary>
    public double DetailsWidth { get; set; } = MainWindowDefaults.DetailsWidth;

    /// <summary>
    /// Stores the current view choices, for example after the details pane was resized.
    /// </summary>
    public void SaveSettings()
    {
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Gets the latest localized monitoring status.
    /// </summary>
    public string Status
    {
        get { return _status; }
        private set { SetProperty(ref _status, value); }
    }

    /// <summary>
    /// Gets or sets a warning about missing hardware access, or null if sensors can be read.
    /// </summary>
    public string HardwareWarning
    {
        get { return _hardwareWarning; }
        set { SetProperty(ref _hardwareWarning, value); }
    }

    /// <summary>
    /// Applies a new sensor snapshot while preserving existing node state.
    /// </summary>
    /// <param name="snapshot">The latest sensor tree snapshot.</param>
    public void Apply(SensorTreeSnapshot snapshot)
    {
        _snapshot = snapshot;
        Mapping.Record(snapshot);
        RecordHistories(snapshot);
        UpdateTree();
        UpdateSelectedHistory();
        UpdateDashboard();

        string format = snapshot.Roots.Count > 0 && snapshot.Roots[0].Children.Count == 0
            ? LanguageResources.StatusUpdatedNoHardwareFormat
            : LanguageResources.StatusUpdatedFormat;

        CultureInfo systemCulture = AppCulture.SystemFormattingCulture;

        Status = string.Format(systemCulture, format, snapshot.Updated.ToString("T", systemCulture));
    }

    /// <summary>
    /// Displays a localized monitoring failure with the underlying error detail.
    /// </summary>
    /// <param name="message">The error detail supplied by hardware monitoring.</param>
    public void ShowError(string message)
    {
        Status = string.Format(CultureInfo.CurrentCulture, LanguageResources.StatusMonitoringUnavailableFormat, message);
    }

    /// <summary>
    /// Updates the visible tree from the latest snapshot.
    /// </summary>
    private void UpdateTree()
    {
        if (_snapshot != null)
        {
            IReadOnlyList<SensorTreeNodeSnapshot> roots = SensorTreeGrouping.GetRoots(_snapshot, SelectedGrouping.Grouping);

            SensorTreeReconciler.Reconcile(Hardware, SensorTreeFilter.Apply(roots, FilterText, SelectedSortOrder.SortOrder, ShowHidden ? null : _hiddenSensors, _groupSortOrders, ShowAdvanced));
            MarkHidden(Hardware);
            SetCaptions(Hardware);
            UpdateDetails();
        }
    }

    /// <summary>
    /// Updates the details of the selected node.
    /// </summary>
    private void UpdateDetails()
    {
        List<DetailViewModel> details = [];

        if (SelectedNode != null && TryFindHardware(Hardware, null, out SensorTreeNodeSnapshot hardware))
        {
            AddDetails(details, SelectedNode.Snapshot);

            if (hardware != null && hardware != SelectedNode.Snapshot)
            {
                AddDetails(details, hardware);
            }
        }

        if (!details.SequenceEqual(Details))
        {
            Details = details;
        }
    }

    /// <summary>
    /// Searches the selected node and the closest hardware that contains it.
    /// </summary>
    /// <param name="nodes">The nodes to search.</param>
    /// <param name="parent">The closest hardware above the nodes.</param>
    /// <param name="hardware">The closest hardware of the selected node, if any.</param>
    /// <returns>True if the selected node was found.</returns>
    private bool TryFindHardware(IEnumerable<SensorTreeNodeViewModel> nodes,
        SensorTreeNodeSnapshot parent,
        out SensorTreeNodeSnapshot hardware)
    {
        foreach (SensorTreeNodeViewModel node in nodes)
        {
            hardware = node.Kind == SensorTreeNodeKind.Hardware ? node.Snapshot : parent;

            if (node == SelectedNode || TryFindHardware(node.Children, hardware, out hardware))
            {
                return true;
            }
        }

        hardware = null;

        return false;
    }

    /// <summary>
    /// Adds the details of a node.
    /// </summary>
    /// <param name="details">The list to fill.</param>
    /// <param name="node">The node to describe.</param>
    private void AddDetails(List<DetailViewModel> details, SensorTreeNodeSnapshot node)
    {
        switch (node.Kind)
        {
            case SensorTreeNodeKind.Hardware:
                details.Add(new DetailViewModel(LanguageResources.DetailHardware, node.Name));
                break;
            case SensorTreeNodeKind.Sensor:
                details.Add(new DetailViewModel(LanguageResources.DetailSensor, node.Name));
                details.Add(new DetailViewModel(LanguageResources.DetailCategory, SensorCategoryName.GetName(node.SensorType.Value)));
                details.Add(new DetailViewModel(LanguageResources.DetailIdentifier, node.Identifier));
                break;
        }

        if (node.Properties != null)
        {
            bool IsAdvanced(KeyValuePair<string, string> property) => AdvancedPropertyPrefixes.Any(prefix => property.Key.StartsWith(prefix, StringComparison.Ordinal));

            details.AddRange(node.Properties.Where(property => !IsAdvanced(property)).Select(property => new DetailViewModel(property.Key, property.Value)));

            if (ShowAdvanced)
            {
                details.AddRange(node.Properties.Where(IsAdvanced).Select((property, index) => new DetailViewModel(property.Key, property.Value, index == 0)));
            }
        }
    }
}
