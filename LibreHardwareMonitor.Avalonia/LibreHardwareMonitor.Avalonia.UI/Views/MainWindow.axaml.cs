// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BlackSharp.UI.Avalonia.Controls;
using BlackSharp.UI.Avalonia.TreeDataGrid.Selection;
using LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;
using LibreHardwareMonitor.Avalonia.UI.MockData;

namespace LibreHardwareMonitor.Avalonia.UI.Views;

/// <summary>
/// Presents hardware and sensors in a hierarchical data grid.
/// </summary>
public sealed partial class MainWindow : DecoratedWindow
{
    /// <summary>
    /// Tracks the selected rows, because the tree only exposes the first selected item.
    /// </summary>
    private readonly HashSet<SensorTreeNodeViewModel> _selectedNodes = new();

    /// <summary>
    /// Initializes the window for XAML preview and runtime construction.
    /// </summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        if (Design.IsDesignMode)
        {
            DataContext = MainWindowDesignData.MainWindow;
        }
    }

    /// <summary>
    /// Initializes the window with live sensor presentation state.
    /// </summary>
    /// <param name="viewModel">The sensor tree and status to display.</param>
    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// Keeps the set of selected rows up to date.
    /// </summary>
    /// <param name="sender">The sensor tree.</param>
    /// <param name="e">The selection change.</param>
    private void OnTreeSelectionChanged(object sender, TreeDataGridSelectionChangedEventArgs e)
    {
        _selectedNodes.ExceptWith(e.DeselectedItems.OfType<SensorTreeNodeViewModel>());
        _selectedNodes.UnionWith(e.SelectedItems.OfType<SensorTreeNodeViewModel>());
    }

    /// <summary>
    /// Hides the selected sensors when Delete is pressed.
    /// </summary>
    /// <param name="sender">The sensor tree.</param>
    /// <param name="e">The key event.</param>
    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            SetSelectedHidden(true);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Hides the selected sensors.
    /// </summary>
    /// <param name="sender">The menu item.</param>
    /// <param name="e">The click event.</param>
    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        SetSelectedHidden(true);
    }

    /// <summary>
    /// Shows the selected sensors again.
    /// </summary>
    /// <param name="sender">The menu item.</param>
    /// <param name="e">The click event.</param>
    private void OnShowClick(object sender, RoutedEventArgs e)
    {
        SetSelectedHidden(false);
    }

    /// <summary>
    /// Hides or shows the selected sensors.
    /// </summary>
    /// <param name="isHidden">True to hide the sensors, false to show them again.</param>
    private void SetSelectedHidden(bool isHidden)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetHidden(_selectedNodes, isHidden);
        }
    }
}
