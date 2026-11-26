// SPDX-License-Identifier: MPL-2.0

using Avalonia;
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

        SizeChanged += (_, e) => UpdateDetailsLayout(e.NewSize.Width);

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
    /// Window width below which the details pane moves below the tree, so the value columns stay visible.
    /// </summary>
    private const double NarrowWidth = 900;

    /// <summary>
    /// Places the details pane beside the tree in wide windows and below it in narrow ones.
    /// </summary>
    /// <param name="width">The current window width.</param>
    private void UpdateDetailsLayout(double width)
    {
        ScrollViewer pane = this.FindControl<ScrollViewer>("DetailsPane");
        if (pane == null)
        {
            return;
        }

        bool isNarrow = width < NarrowWidth;

        Grid.SetRow(pane, isNarrow ? 2 : 1);
        Grid.SetColumn(pane, isNarrow ? 0 : 1);
        Grid.SetColumnSpan(pane, isNarrow ? 2 : 1);
        pane.Width = isNarrow ? double.NaN : 260;
        pane.MaxHeight = isNarrow ? 200 : double.PositiveInfinity;
        pane.Margin = isNarrow ? new Thickness(12, 0, 12, 12) : new Thickness(0, 12, 12, 12);
    }

    /// <summary>
    /// Opens the sensor mapping assistant.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click event.</param>
    private void OnMappingClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            new SensorMappingWindow(viewModel.Mapping).Show(this);
        }
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
