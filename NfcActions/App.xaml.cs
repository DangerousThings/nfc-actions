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
    private System.ComponentModel.CancelEventHandler? _windowClosingHandler;

    public App()
    {
        // CRITICAL: Set software rendering mode BEFORE any WPF initialization
        // This must be done in the constructor before InitializeComponent()
        try
        {
            // Set environment variable as additional safeguard
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_WINDOWS_DONOTUSEPRESENTATIONDPICAPABILITYTIER2ORGREATER", "1");

            // Force software rendering
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }
        catch
        {
            // If this fails, try to continue anyway
        }
    }

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
        // If window was closed, recreate it
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            if (_viewModel == null) return;

            _mainWindow = new MainWindow(_viewModel);

            // Handle window closing - hide instead of close
            _windowClosingHandler = (s, e) =>
            {
                e.Cancel = true;
                if (_mainWindow != null)
                {
                    _mainWindow.Hide();
                }
            };

            _mainWindow.Closing += _windowClosingHandler;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        // Properly close the main window if it exists
        if (_mainWindow != null)
        {
            // Remove the cancel handler so the window can actually close
            if (_windowClosingHandler != null)
            {
                _mainWindow.Closing -= _windowClosingHandler;
            }
            _mainWindow.Close();
        }

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
