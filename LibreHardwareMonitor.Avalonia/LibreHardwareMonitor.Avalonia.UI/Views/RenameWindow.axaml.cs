// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LibreHardwareMonitor.Avalonia.UI.Views;

/// <summary>
/// Asks for a new name. The dialog result is the name, an empty string for the default name, or null when canceled.
/// </summary>
public sealed partial class RenameWindow : Window
{
    private readonly TextBox _nameBox;

    /// <summary>
    /// Initializes the dialog for XAML preview.
    /// </summary>
    public RenameWindow() : this(string.Empty)
    {
    }

    /// <summary>
    /// Initializes the dialog with the current name.
    /// </summary>
    /// <param name="name">The current name.</param>
    public RenameWindow(string name)
    {
        AvaloniaXamlLoader.Load(this);

        _nameBox = this.FindControl<TextBox>("NameBox");
        _nameBox.Text = name;
        Opened += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
    }

    /// <summary>
    /// Returns the entered name.
    /// </summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Close(_nameBox.Text?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Returns an empty name, which restores the default name.
    /// </summary>
    private void OnDefaultClick(object sender, RoutedEventArgs e)
    {
        Close(string.Empty);
    }

    /// <summary>
    /// Closes without renaming.
    /// </summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
