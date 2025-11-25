using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using NfcActions.Services;
using NfcActions.ViewModels;

namespace NfcActions;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Set version number from assembly
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            var versionString = $"v{version.Major}.{version.Minor}.{version.Build}";
            VersionText.Text = versionString;
            Title = $"NFC Actions {versionString}";
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }

    private void Logo_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://dangerousthings.com",
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently fail if browser can't be opened
        }
    }

    private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedItems = LogListBox.SelectedItems.Cast<LogEntry>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("No log entries selected. Please select one or more lines first.",
                               "No Selection",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
                return;
            }

            var logText = new StringBuilder();
            foreach (var entry in selectedItems)
            {
                logText.AppendLine(entry.FormattedMessage);
            }

            Clipboard.SetText(logText.ToString());

            MessageBox.Show($"Copied {selectedItems.Count} log {(selectedItems.Count == 1 ? "entry" : "entries")} to clipboard.",
                           "Copied",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy to clipboard: {ex.Message}",
                           "Error",
                           MessageBoxButton.OK,
                           MessageBoxImage.Error);
        }
    }

    private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel viewModel)
            {
                var allEntries = viewModel.LogEntries.ToList();
                if (allEntries.Count == 0)
                {
                    MessageBox.Show("No log entries available.",
                                   "Empty Log",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
                    return;
                }

                var logText = new StringBuilder();
                foreach (var entry in allEntries)
                {
                    logText.AppendLine(entry.FormattedMessage);
                }

                Clipboard.SetText(logText.ToString());

                MessageBox.Show($"Copied all {allEntries.Count} log entries to clipboard.",
                               "Copied",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy to clipboard: {ex.Message}",
                           "Error",
                           MessageBoxButton.OK,
                           MessageBoxImage.Error);
        }
    }

    private void SelectAllLogs_Click(object sender, RoutedEventArgs e)
    {
        LogListBox.SelectAll();
    }
}
