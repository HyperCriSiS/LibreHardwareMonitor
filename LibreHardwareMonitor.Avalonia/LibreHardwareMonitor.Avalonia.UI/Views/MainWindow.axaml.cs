// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BlackSharp.UI.Avalonia.Controls;
using BlackSharp.UI.Avalonia.TreeDataGrid.Selection;
using LibreHardwareMonitor.Avalonia.Mvvm.Services;
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
    /// Hides the window to the notification area when it is minimized and the option is on.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty &&
            WindowState == WindowState.Minimized &&
            DataContext is MainWindowViewModel { IsTrayIconEnabled: true, IsMinimizeToTray: true })
        {
            Hide();
        }
    }

    /// <summary>
    /// Shows the window again, for example after a click on the notification area icon.
    /// </summary>
    public void Restore()
    {
        Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
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
        double detailsWidth = DataContext is MainWindowViewModel viewModel ? ClampDetailsWidth(viewModel.DetailsWidth, width) : MainWindowDefaults.DetailsWidth;

        if (this.FindControl<Border>("DetailsSplitter") is { } splitter)
        {
            // Below the tree the pane takes the full width, so there is nothing to drag.
            splitter.IsHitTestVisible = !isNarrow;
            splitter.Opacity = isNarrow ? 0 : 1;
        }

        Grid.SetRow(pane, isNarrow ? 2 : 1);
        Grid.SetColumn(pane, isNarrow ? 0 : 1);
        Grid.SetColumnSpan(pane, isNarrow ? 2 : 1);
        pane.Width = isNarrow ? double.NaN : detailsWidth;
        pane.MaxHeight = isNarrow ? 200 : double.PositiveInfinity;
        pane.Margin = isNarrow ? new Thickness(12, 0, 12, 12) : new Thickness(0, 12, 12, 12);
    }

    /// <summary>
    /// The narrowest details pane and the width the tree keeps at least.
    /// </summary>
    private const double MinDetailsWidth = 200;

    private const double MinTreeWidth = 400;

    private double _dragStartX;
    private double _dragStartWidth;
    private bool _isDraggingDetails;

    /// <summary>
    /// Keeps the details pane between its minimum width and the space the tree needs.
    /// </summary>
    private static double ClampDetailsWidth(double width, double windowWidth)
    {
        double max = Math.Max(MinDetailsWidth, windowWidth - MinTreeWidth);
        return Math.Clamp(width, MinDetailsWidth, max);
    }

    /// <summary>
    /// Starts resizing the details pane.
    /// </summary>
    private void OnDetailsSplitterPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is Border splitter && splitter.IsHitTestVisible && this.FindControl<ScrollViewer>("DetailsPane") is { } pane &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDraggingDetails = true;
            _dragStartX = e.GetPosition(this).X;
            _dragStartWidth = pane.Bounds.Width;
            e.Pointer.Capture(splitter);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Resizes the details pane while dragging; moving left makes it wider.
    /// </summary>
    private void OnDetailsSplitterMoved(object sender, PointerEventArgs e)
    {
        if (_isDraggingDetails && this.FindControl<ScrollViewer>("DetailsPane") is { } pane)
        {
            pane.Width = ClampDetailsWidth(_dragStartWidth + (_dragStartX - e.GetPosition(this).X), Bounds.Width);
        }
    }

    /// <summary>
    /// Finishes resizing and stores the width.
    /// </summary>
    private void OnDetailsSplitterReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingDetails)
            return;

        _isDraggingDetails = false;
        e.Pointer.Capture(null);

        if (DataContext is MainWindowViewModel viewModel && this.FindControl<ScrollViewer>("DetailsPane") is { } pane)
        {
            viewModel.DetailsWidth = pane.Width;
            viewModel.SaveSettings();
        }
    }

    /// <summary>
    /// Shows the anonymized hardware report for review before it is saved or copied.
    /// </summary>
    private async void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { ReportProvider: { } provider })
        {
            new ReportWindow(await provider()).Show(this);
        }
    }

    /// <summary>
    /// Opens the folder with the CSV logs in the file manager.
    /// </summary>
    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Application.App.LogDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.App.LogDirectory) { UseShellExecute = true });
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
            new SensorMappingWindow(viewModel).Show(this);
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
        if (e.Key == Key.F2)
        {
            RenameSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            SetSelectedHidden(true);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Expands everything below the clicked node, or collapses it if it is fully expanded.
    /// </summary>
    /// <param name="sender">The button in the row.</param>
    /// <param name="e">The click event.</param>
    private void OnToggleAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SensorTreeNodeViewModel node })
        {
            node.ToggleExpandedRecursive();
        }
    }

    /// <summary>
    /// Expands everything below the selected nodes.
    /// </summary>
    /// <param name="sender">The menu item.</param>
    /// <param name="e">The click event.</param>
    private void OnExpandAllClick(object sender, RoutedEventArgs e)
    {
        foreach (SensorTreeNodeViewModel node in _selectedNodes.ToList())
        {
            node.SetExpandedRecursive(true);
        }
    }

    /// <summary>
    /// Collapses everything below the selected nodes.
    /// </summary>
    /// <param name="sender">The menu item.</param>
    /// <param name="e">The click event.</param>
    private void OnCollapseAllClick(object sender, RoutedEventArgs e)
    {
        foreach (SensorTreeNodeViewModel node in _selectedNodes.ToList())
        {
            node.SetExpandedRecursive(false);
        }
    }

    /// <summary>
    /// Sorts the sensors of one category by the clicked column.
    /// </summary>
    /// <param name="sender">The caption button.</param>
    /// <param name="e">The click event.</param>
    private void OnColumnCaptionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is Button { DataContext: SensorTreeNodeViewModel node, Tag: string column })
        {
            viewModel.ToggleGroupSort(node, column);
        }
    }

    /// <summary>
    /// Renames the selected sensor or hardware.
    /// </summary>
    /// <param name="sender">The menu item.</param>
    /// <param name="e">The click event.</param>
    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        RenameSelected();
    }

    /// <summary>
    /// Asks for a new name of the selected sensor or hardware.
    /// </summary>
    private async void RenameSelected()
    {
        if (DataContext is MainWindowViewModel viewModel && MainWindowViewModel.CanRename(viewModel.SelectedNode))
        {
            SensorTreeNodeViewModel node = viewModel.SelectedNode;
            string name = await new RenameWindow(node.Name).ShowDialog<string>(this);

            if (name != null)
            {
                viewModel.Rename(node, name);
            }
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
    /// Pins the selected sensors to the dashboard.
    /// </summary>
    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.SetPinned(_selectedNodes, true);
    }

    /// <summary>
    /// Removes the selected sensors from the dashboard.
    /// </summary>
    private void OnUnpinClick(object sender, RoutedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.SetPinned(_selectedNodes, false);
    }

    /// <summary>
    /// Removes a tile from the dashboard.
    /// </summary>
    private void OnUnpinTileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DashboardTileViewModel tile)
        {
            (DataContext as MainWindowViewModel)?.SetPinned([tile.Identifier], false);
        }
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
