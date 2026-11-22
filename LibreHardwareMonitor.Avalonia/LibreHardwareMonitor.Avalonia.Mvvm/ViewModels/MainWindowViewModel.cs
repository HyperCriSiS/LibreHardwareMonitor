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
        }
    }
}
