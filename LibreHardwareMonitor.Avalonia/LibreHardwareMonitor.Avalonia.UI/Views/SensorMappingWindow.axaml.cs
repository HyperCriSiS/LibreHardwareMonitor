// SPDX-License-Identifier: MPL-2.0

using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BlackSharp.UI.Avalonia.Controls;
using LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

namespace LibreHardwareMonitor.Avalonia.UI.Views;

/// <summary>
/// Guides through a recording that suggests what motherboard temperatures measure.
/// </summary>
public sealed partial class SensorMappingWindow : DecoratedWindow
{
    /// <summary>
    /// Initializes the window for XAML preview and runtime construction.
    /// </summary>
    public SensorMappingWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Closing the window must never leave the CPU under load.
        Closed += (_, _) => (DataContext as SensorMappingViewModel)?.Cancel();
    }

    /// <summary>
    /// Initializes the window with the assistant state.
    /// </summary>
    /// <param name="viewModel">The assistant state.</param>
    public SensorMappingWindow(SensorMappingViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// Starts a recording.
    /// </summary>
    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        (DataContext as SensorMappingViewModel)?.Start();
    }

    /// <summary>
    /// Cancels the recording.
    /// </summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        (DataContext as SensorMappingViewModel)?.Cancel();
    }
}
