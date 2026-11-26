// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
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

    private readonly MainWindowViewModel _main;

    /// <summary>
    /// Initializes the window with the assistant state of the main window.
    /// </summary>
    /// <param name="main">The main window state, which renames and hides sensors.</param>
    public SensorMappingWindow(MainWindowViewModel main) : this()
    {
        _main = main;
        DataContext = main.Mapping;
    }

    /// <summary>
    /// Renames the sensor of a result, starting with the suggested name.
    /// </summary>
    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SensorMappingResultViewModel result && _main != null)
        {
            string name = await new RenameWindow(result.SuggestedName).ShowDialog<string>(this);

            if (name != null)
            {
                _main.RenameSensor(result.Identifier, name);
            }
        }
    }

    /// <summary>
    /// Hides the sensor of a result.
    /// </summary>
    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SensorMappingResultViewModel result && _main != null)
        {
            _main.HideSensor(result.Identifier);
            ((Button)sender).IsEnabled = false;
        }
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
