using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace NfcActions.Services;

public class LogService
{
    private readonly SynchronizationContext? _syncContext;
    private readonly string? _logFilePath;
    private readonly object _fileLock = new();
    private readonly bool _fileLoggingEnabled;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    private const int MAX_LOG_ENTRIES = 500;

    public LogService(bool enableFileLogging = false)
    {
        _syncContext = SynchronizationContext.Current;
        _fileLoggingEnabled = enableFileLogging;

        if (_fileLoggingEnabled)
        {
            // Create log file in the app directory (works for single-file publish)
            var exeDir = AppContext.BaseDirectory;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logFilePath = Path.Combine(exeDir, $"nfc-actions-debug-{timestamp}.log");

            // Write initial header
            WriteToFile($"=== NFC Actions Debug Log - Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            WriteToFile($"Log file: {_logFilePath}");
            WriteToFile("");
        }
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Level = level
        };

        // Write to file immediately
        WriteToFile($"[{entry.Timestamp:HH:mm:ss.fff}] [{level}] {message}");

        // Update UI
        if (_syncContext != null)
        {
            _syncContext.Post(_ => AddEntry(entry), null);
        }
        else
        {
            Application.Current?.Dispatcher.Invoke(() => AddEntry(entry));
        }
    }

    private void WriteToFile(string message)
    {
        if (!_fileLoggingEnabled || _logFilePath == null)
            return;

        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, message + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore file write errors
        }
    }

    private void AddEntry(LogEntry entry)
    {
        LogEntries.Insert(0, entry);

        // Keep only the last MAX_LOG_ENTRIES
        while (LogEntries.Count > MAX_LOG_ENTRIES)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    public void Debug(string message) => Log(message, LogLevel.Debug);
    public void Info(string message) => Log(message, LogLevel.Info);
    public void Warning(string message) => Log(message, LogLevel.Warning);
    public void Error(string message) => Log(message, LogLevel.Error);
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; }

    public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] {Message}";
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
