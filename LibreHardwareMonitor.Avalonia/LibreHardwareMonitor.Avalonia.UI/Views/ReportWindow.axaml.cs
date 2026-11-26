// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace LibreHardwareMonitor.Avalonia.UI.Views;

/// <summary>
/// Shows a hardware report before it is saved or copied, so nothing leaves the computer unseen.
/// </summary>
public sealed partial class ReportWindow : Window
{
    private readonly TextBox _reportBox;

    /// <summary>
    /// Initializes the window for XAML preview.
    /// </summary>
    public ReportWindow() : this(string.Empty)
    {
    }

    /// <summary>
    /// Initializes the window with a report.
    /// </summary>
    /// <param name="report">The report to show.</param>
    public ReportWindow(string report)
    {
        AvaloniaXamlLoader.Load(this);

        _reportBox = this.FindControl<TextBox>("ReportBox");
        _reportBox.Text = report;
    }

    /// <summary>
    /// Copies the report to the clipboard.
    /// </summary>
    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_reportBox.Text);
        }
    }

    /// <summary>
    /// Saves the report to a text file the user chooses.
    /// </summary>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        IStorageFile file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "LibreHardwareMonitor.Report.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [FilePickerFileTypes.TextPlain]
        });

        if (file == null)
            return;

        await using Stream stream = await file.OpenWriteAsync();
        await using StreamWriter writer = new(stream);
        await writer.WriteAsync(_reportBox.Text);
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
