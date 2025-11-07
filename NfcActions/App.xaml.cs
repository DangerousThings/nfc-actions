using System;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using NfcActions.Services;
using NfcActions.ViewModels;
using Application = System.Windows.Application;

namespace NfcActions;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _instanceMutex;
    private NotifyIcon? _notifyIcon;
    private Icon? _customIcon;
    private MainWindow? _mainWindow;
    private CardReaderService? _cardReaderService;
    private SettingsService? _settingsService;
    private ActionService? _actionService;
    private LogService? _logService;
    private MainViewModel? _viewModel;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Ensure only one instance runs at a time
        bool createdNew;
        _instanceMutex = new Mutex(true, "NfcActions_SingleInstance_Mutex", out createdNew);

        if (!createdNew)
        {
            System.Windows.MessageBox.Show("NFC Actions is already running. Check the system tray.",
                                          "NFC Actions",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Workaround for DirectWrite crash - force software rendering
        try
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }
        catch
        {
            // Ignore if this fails
        }
        // Initialize services
        _logService = new LogService();
        _cardReaderService = new CardReaderService(_logService);
        _settingsService = new SettingsService();
        _actionService = new ActionService();

        // Initialize ViewModel
        _viewModel = new MainViewModel(_cardReaderService, _settingsService, _actionService, _logService);

        // Create main window but don't show it yet
        _mainWindow = new MainWindow(_viewModel);

        // Load custom icon
        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _customIcon = new Icon(iconPath);
            }
        }
        catch
        {
            // Fall back to default if custom icon can't be loaded
        }

        // Create system tray icon
        _notifyIcon = new NotifyIcon
        {
            Icon = _customIcon ?? SystemIcons.Application,
            Visible = true,
            Text = "NFC Actions"
        };

        // Set up context menu
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open", null, (s, args) => ShowMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, args) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.MouseClick += (s, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };

        // Start the card reader service
        _cardReaderService.Start();

        // Show a notification that the app is running
        _notifyIcon.ShowBalloonTip(
            3000,
            "NFC Actions",
            "NFC Actions is now monitoring for card events. Right-click the tray icon to configure.",
            ToolTipIcon.Info);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        _notifyIcon?.Dispose();
        _customIcon?.Dispose();
        _cardReaderService?.Dispose();
        Shutdown();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _customIcon?.Dispose();
        _cardReaderService?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
    }
}
