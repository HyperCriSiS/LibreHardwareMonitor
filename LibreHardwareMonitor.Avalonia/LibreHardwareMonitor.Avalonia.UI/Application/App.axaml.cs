// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Avalonia.Monitoring.Services;
using LibreHardwareMonitor.Avalonia.Mvvm.Services;
using LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;
using LibreHardwareMonitor.Avalonia.UI.MockData;
using LibreHardwareMonitor.Avalonia.UI.Services;
using LibreHardwareMonitor.Avalonia.UI.Views;
using AvaloniaApplication = Avalonia.Application;
using LanguageResources = LibreHardwareMonitor.Avalonia.Localization.Resources.Resources;

namespace LibreHardwareMonitor.Avalonia.UI.Application;

/// <summary>
/// Creates the main window and starts sensor monitoring for the desktop application.
/// </summary>
public sealed partial class App : AvaloniaApplication
{
    /// <summary>
    /// Keeps the sensor monitor alive for the desktop application's lifetime.
    /// </summary>
    private SensorMonitor _monitor;
    private SensorWebServer _webServer;
    private volatile SensorTreeSnapshot _lastSnapshot;
    private (bool Enabled, bool LocalOnly, int Port) _webServerConfiguration;

    /// <summary>
    /// Loads the application styles and resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        if (Design.IsDesignMode)
        {
            Global.FontManager = MainWindowDesignData.FontService;

            RequestedThemeVariant = ThemeVariant.Dark;
        }
        else
        {
            Global.FontManager = new FontService();
        }
    }

    /// <summary>
    /// Starts, restarts or stops the web server to match the settings.
    /// </summary>
    /// <param name="viewModel">The main window state with the web server settings.</param>
    private void UpdateWebServer(MainWindowViewModel viewModel)
    {
        (bool Enabled, bool LocalOnly, int Port) wanted = (viewModel.IsWebServerEnabled, viewModel.IsWebServerLocalOnly, viewModel.WebServerPort);
        if (wanted == _webServerConfiguration)
            return;

        _webServerConfiguration = wanted;
        _webServer?.Dispose();
        _webServer = null;

        if (!wanted.Enabled)
        {
            viewModel.WebServerStatus = string.Empty;
            return;
        }

        try
        {
            _webServer = new SensorWebServer(wanted.Port, wanted.LocalOnly, _lastSnapshot);
            viewModel.WebServerStatus = _webServer.DataAddress;
        }
        catch (Exception exception)
        {
            viewModel.WebServerStatus = string.Format(CultureInfo.CurrentCulture, LanguageResources.WebServerFailedFormat, exception.Message);
        }
    }

    /// <summary>
    /// Creates the main window and connects live sensor updates.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindowViewModel viewModel = new();
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LibreHardwareMonitor",
                "LibreHardwareMonitor.Avalonia.json");

            viewModel.ApplySettings(ViewSettings.Load(settingsPath));
            viewModel.SettingsChanged += () => viewModel.GetSettings().Save(settingsPath);

            // Without PawnIO the CPU, motherboard and memory sensors stay empty without any other hint.
            viewModel.HardwareWarning = !PawnIo.PawnIo.IsInstalled
                ? LanguageResources.WarningPawnIoMissing
                : PawnIo.PawnIo.Version < new Version(2, 0, 0, 0)
                    ? LanguageResources.WarningPawnIoOutdated
                    : null;

            desktop.MainWindow = new MainWindow(viewModel);

            _monitor = new SensorMonitor(new JsonSettings(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LibreHardwareMonitor",
                "LibreHardwareMonitor.Avalonia.Sensors.json")));
            viewModel.RenameRequested += _monitor.Rename;
            viewModel.ReportProvider = _monitor.CreateReportAsync;

            _monitor.SnapshotAvailable += snapshot => Dispatcher.UIThread.Post(() => viewModel.Apply(snapshot));
            _monitor.SnapshotAvailable += snapshot =>
            {
                _lastSnapshot = snapshot;
                _webServer?.Update(snapshot);
            };
            viewModel.SettingsChanged += () => UpdateWebServer(viewModel);
            UpdateWebServer(viewModel);
            _monitor.ErrorOccurred += message => Dispatcher.UIThread.Post(() => viewModel.ShowError(message));

            desktop.Exit += (_, _) =>
            {
                _monitor.Dispose();
                _webServer?.Dispose();
            };

            _monitor.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
