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
    private string _status = LanguageResources.StatusSearching;

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
            }
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
            }
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
        set { SetProperty(ref _isDetailsVisible, value); }
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
    /// Applies a new sensor snapshot while preserving existing node state.
    /// </summary>
    /// <param name="snapshot">The latest sensor tree snapshot.</param>
    public void Apply(SensorTreeSnapshot snapshot)
    {
        _snapshot = snapshot;
        UpdateTree();

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
            SensorTreeReconciler.Reconcile(Hardware, SensorTreeGrouping.GetRoots(_snapshot, SelectedGrouping.Grouping));
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
    private static void AddDetails(List<DetailViewModel> details, SensorTreeNodeSnapshot node)
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
            details.AddRange(node.Properties.Select(property => new DetailViewModel(property.Key, property.Value)));
        }
    }
}
