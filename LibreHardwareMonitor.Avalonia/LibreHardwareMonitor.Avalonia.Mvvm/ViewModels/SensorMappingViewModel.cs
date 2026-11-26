// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Avalonia.Monitoring.Services;
using LanguageResources = LibreHardwareMonitor.Avalonia.Localization.Resources.Resources;

namespace LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

/// <summary>
/// Runs a recording at idle and under CPU load and shows what the motherboard temperatures likely measure.
/// </summary>
public sealed class SensorMappingViewModel : ObservableObject
{
    /// <summary>
    /// Number of snapshots recorded at idle. Snapshots arrive once per second.
    /// </summary>
    public const int IdleSnapshots = 20;

    /// <summary>
    /// Number of snapshots recorded under CPU load.
    /// </summary>
    public const int LoadSnapshots = 60;

    private readonly List<SensorTreeSnapshot> _idle = [];
    private readonly List<SensorTreeSnapshot> _load = [];
    private CancellationTokenSource _stress;
    private bool _isRunning;
    private double _progress;
    private string _status = LanguageResources.MappingIntroduction;
    private IReadOnlyList<SensorMappingResultViewModel> _results = [];

    /// <summary>
    /// Gets whether a recording is running.
    /// </summary>
    public bool IsRunning
    {
        get { return _isRunning; }
        private set { SetProperty(ref _isRunning, value); }
    }

    /// <summary>
    /// Gets the progress of the recording from 0 to 100.
    /// </summary>
    public double Progress
    {
        get { return _progress; }
        private set { SetProperty(ref _progress, value); }
    }

    /// <summary>
    /// Gets the current step or result summary.
    /// </summary>
    public string Status
    {
        get { return _status; }
        private set { SetProperty(ref _status, value); }
    }

    /// <summary>
    /// Gets the suggestions of the last recording.
    /// </summary>
    public IReadOnlyList<SensorMappingResultViewModel> Results
    {
        get { return _results; }
        private set { SetProperty(ref _results, value); }
    }

    /// <summary>
    /// Starts a new recording.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _idle.Clear();
        _load.Clear();
        Results = [];
        Progress = 0;
        Status = LanguageResources.MappingIdle;
        IsRunning = true;
    }

    /// <summary>
    /// Cancels a running recording and stops the CPU load.
    /// </summary>
    public void Cancel()
    {
        StopStress();

        if (IsRunning)
        {
            IsRunning = false;
            Status = LanguageResources.MappingCanceled;
        }
    }

    /// <summary>
    /// Records a snapshot while a recording is running.
    /// </summary>
    /// <param name="snapshot">The latest sensor tree snapshot.</param>
    public void Record(SensorTreeSnapshot snapshot)
    {
        if (!IsRunning)
        {
            return;
        }

        if (_idle.Count < IdleSnapshots)
        {
            _idle.Add(snapshot);

            if (_idle.Count == IdleSnapshots)
            {
                StartStress();
                Status = LanguageResources.MappingLoad;
            }
        }
        else
        {
            _load.Add(snapshot);

            if (_load.Count == LoadSnapshots)
            {
                StopStress();
                IsRunning = false;
                ShowResults(SensorMappingAnalyzer.Analyze(_idle, _load));
            }
        }

        Progress = 100.0 * (_idle.Count + _load.Count) / (IdleSnapshots + LoadSnapshots);
    }

    /// <summary>
    /// Shows the suggestions of a finished recording.
    /// </summary>
    /// <param name="suggestions">The suggestions to show.</param>
    private void ShowResults(IReadOnlyList<SensorMappingSuggestion> suggestions)
    {
        Results = suggestions.Select(suggestion => new SensorMappingResultViewModel(
                                 suggestion.Identifier,
                                 GetSuggestedName(suggestion),
                                 suggestion.Kind is SensorMappingKind.NotConnected or SensorMappingKind.Constant,
                                 suggestion.HardwareName + ": " + suggestion.Name,
                                 GetDescription(suggestion.Kind),
                                 suggestion.Rise.HasValue
                                     ? string.Format(CultureInfo.CurrentCulture, LanguageResources.MappingRiseFormat, suggestion.Rise.Value)
                                     : string.Empty,
                                 string.IsNullOrEmpty(suggestion.SameValueAs)
                                     ? string.Empty
                                     : string.Format(CultureInfo.CurrentCulture, LanguageResources.MappingSameValueFormat, suggestion.SameValueAs)))
                             .ToList();

        Status = suggestions.Count > 0 ? LanguageResources.MappingDone : LanguageResources.MappingNoSensors;
    }

    /// <summary>
    /// Gets a name to start renaming with. Sensor names stay in English like the names from the hardware library.
    /// </summary>
    private static string GetSuggestedName(SensorMappingSuggestion suggestion)
    {
        switch (suggestion.Kind)
        {
            case SensorMappingKind.FollowsCpu: return "CPU Socket";
            case SensorMappingKind.WarmsWithCpu: return "VRM";
            case SensorMappingKind.IndependentOfCpu: return "System";
            default: return suggestion.Name;
        }
    }

    /// <summary>
    /// Gets the localized description of a suggestion.
    /// </summary>
    private static string GetDescription(SensorMappingKind kind)
    {
        switch (kind)
        {
            case SensorMappingKind.NotConnected: return LanguageResources.MappingNotConnected;
            case SensorMappingKind.Constant: return LanguageResources.MappingConstant;
            case SensorMappingKind.FollowsCpu: return LanguageResources.MappingFollowsCpu;
            case SensorMappingKind.WarmsWithCpu: return LanguageResources.MappingWarmsWithCpu;
            case SensorMappingKind.IndependentOfCpu: return LanguageResources.MappingIndependentOfCpu;
            default: return LanguageResources.MappingUnknown;
        }
    }

    /// <summary>
    /// Loads all logical processors with busy threads.
    /// </summary>
    private void StartStress()
    {
        _stress = new CancellationTokenSource();
        CancellationToken token = _stress.Token;

        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            Thread thread = new(() =>
            {
                double value = 1;
                while (!token.IsCancellationRequested)
                {
                    value = Math.Sqrt(value + 1);
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "Sensor mapping load"
            };

            thread.Start();
        }
    }

    /// <summary>
    /// Stops the CPU load.
    /// </summary>
    private void StopStress()
    {
        _stress?.Cancel();
        _stress?.Dispose();
        _stress = null;
    }
}

/// <summary>
/// Represents one suggestion of the sensor mapping assistant.
/// </summary>
/// <param name="Identifier">The sensor identifier.</param>
/// <param name="SuggestedName">A name to start renaming with.</param>
/// <param name="CanHide">Whether the sensor looks unused and hiding it is offered.</param>
/// <param name="Sensor">The sensor and its chip.</param>
/// <param name="Description">What the sensor likely measures.</param>
/// <param name="Rise">The measured rise under load.</param>
/// <param name="SameValue">A sensor of another chip with the same reading.</param>
public sealed record SensorMappingResultViewModel(string Identifier,
    string SuggestedName,
    bool CanHide,
    string Sensor,
    string Description,
    string Rise,
    string SameValue);
