// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

namespace LibreHardwareMonitor.Avalonia.UI.Views;

/// <summary>
/// A small window that stays on top and shows the pinned sensors.
/// </summary>
public sealed partial class GadgetWindow : Window
{
    /// <summary>
    /// Initializes the window for XAML preview.
    /// </summary>
    public GadgetWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Initializes the window with the main window state and places it where it was last.
    /// </summary>
    /// <param name="viewModel">The main window state with the pinned sensors.</param>
    public GadgetWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        if (viewModel.GadgetPosition is { } position)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(position.X, position.Y);
        }

        PositionChanged += (_, e) => viewModel.GadgetPosition = (e.Point.X, e.Point.Y);
    }

    /// <summary>
    /// Moves the window while the left button is held.
    /// </summary>
    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// Turns the gadget off.
    /// </summary>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsGadgetVisible = false;
        }
    }
}
