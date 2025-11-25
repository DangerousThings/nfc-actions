using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using NfcActions.Models;
using NfcActions.Services;

namespace NfcActions.ViewModels;

public class SuffixOption
{
    public SuffixType Value { get; set; }
    public string DisplayName { get; set; } = "";
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly CardReaderService _cardReaderService;
    private readonly SettingsService _settingsService;
    private readonly ActionService _actionService;
    private readonly LogService _logService;
    private AppSettings _settings;

    private bool _copyToClipboard;
    private bool _launchUrls;
    private bool _typeAsKeyboard;
    private SuffixOption _selectedSuffix = null!;

    public ObservableCollection<ReaderItem> Readers { get; } = new();

    public ObservableCollection<SuffixOption> SuffixOptions { get; } = new()
    {
        new SuffixOption { Value = SuffixType.None, DisplayName = "(none)" },
        new SuffixOption { Value = SuffixType.Enter, DisplayName = "[Enter]" },
        new SuffixOption { Value = SuffixType.Tab, DisplayName = "[Tab]" },
        new SuffixOption { Value = SuffixType.Comma, DisplayName = "Comma (,)" },
        new SuffixOption { Value = SuffixType.Colon, DisplayName = "Colon (:)" },
        new SuffixOption { Value = SuffixType.Semicolon, DisplayName = "Semicolon (;)" },
        new SuffixOption { Value = SuffixType.Period, DisplayName = "Period (.)" }
    };

    public bool CopyToClipboard
    {
        get => _copyToClipboard;
        set
        {
            if (_copyToClipboard != value)
            {
                _copyToClipboard = value;
                OnPropertyChanged(nameof(CopyToClipboard));
                _settings.CopyToClipboard = value;
                _settingsService.Save(_settings);
            }
        }
    }

    public bool LaunchUrls
    {
        get => _launchUrls;
        set
        {
            if (_launchUrls != value)
            {
                _launchUrls = value;
                OnPropertyChanged(nameof(LaunchUrls));
                _settings.LaunchUrls = value;
                _settingsService.Save(_settings);
            }
        }
    }

    public bool TypeAsKeyboard
    {
        get => _typeAsKeyboard;
        set
        {
            if (_typeAsKeyboard != value)
            {
                _typeAsKeyboard = value;
                OnPropertyChanged(nameof(TypeAsKeyboard));
                _settings.TypeAsKeyboard = value;
                _settingsService.Save(_settings);
            }
        }
    }

    public SuffixOption SelectedSuffix
    {
        get => _selectedSuffix;
        set
        {
            if (_selectedSuffix != value && value != null)
            {
                _selectedSuffix = value;
                OnPropertyChanged(nameof(SelectedSuffix));
                _settings.Suffix = value.Value;
                _settingsService.Save(_settings);
            }
        }
    }

    public ObservableCollection<LogEntry> LogEntries => _logService.LogEntries;

    public MainViewModel(
        CardReaderService cardReaderService,
        SettingsService settingsService,
        ActionService actionService,
        LogService logService)
    {
        _cardReaderService = cardReaderService;
        _settingsService = settingsService;
        _actionService = actionService;
        _logService = logService;

        // Load settings
        _settings = _settingsService.Load();
        _copyToClipboard = _settings.CopyToClipboard;
        _launchUrls = _settings.LaunchUrls;
        _typeAsKeyboard = _settings.TypeAsKeyboard;
        _selectedSuffix = SuffixOptions.FirstOrDefault(s => s.Value == _settings.Suffix) ?? SuffixOptions[0];

        // Set up event handlers
        _cardReaderService.ReaderAdded += OnReaderAdded;
        _cardReaderService.ReaderRemoved += OnReaderRemoved;
        _cardReaderService.CardInserted += OnCardInserted;

        // Set disabled readers from settings
        _cardReaderService.SetDisabledReaders(_settings.DisabledReaders);

        // Load current readers
        LoadReaders();
    }

    private void LoadReaders()
    {
        Readers.Clear();
        var readers = _cardReaderService.GetAvailableReaders();

        foreach (var readerName in readers)
        {
            var item = new ReaderItem
            {
                Name = readerName,
                IsEnabled = _cardReaderService.IsReaderEnabled(readerName)
            };

            item.PropertyChanged += OnReaderItemPropertyChanged;
            Readers.Add(item);
        }
    }

    private void OnReaderAdded(object? sender, ReaderEventArgs e)
    {
        var existing = Readers.FirstOrDefault(r => r.Name == e.ReaderName);
        if (existing == null)
        {
            var item = new ReaderItem
            {
                Name = e.ReaderName,
                IsEnabled = _cardReaderService.IsReaderEnabled(e.ReaderName)
            };

            item.PropertyChanged += OnReaderItemPropertyChanged;
            Readers.Add(item);
        }
    }

    private void OnReaderRemoved(object? sender, ReaderEventArgs e)
    {
        var item = Readers.FirstOrDefault(r => r.Name == e.ReaderName);
        if (item != null)
        {
            item.PropertyChanged -= OnReaderItemPropertyChanged;
            Readers.Remove(item);
        }
    }

    private void OnReaderItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ReaderItem item && e.PropertyName == nameof(ReaderItem.IsEnabled))
        {
            if (item.IsEnabled)
            {
                _cardReaderService.EnableReader(item.Name);
                _settings.DisabledReaders.Remove(item.Name);
            }
            else
            {
                _cardReaderService.DisableReader(item.Name);
                _settings.DisabledReaders.Add(item.Name);
            }

            _settingsService.Save(_settings);
        }
    }

    private void OnCardInserted(object? sender, CardEventArgs e)
    {
        if (e.CardData == null)
        {
            _logService.Warning("Card inserted but no data available");
            return;
        }

        _logService.Info($"Processing card data ({e.CardData.Length} bytes)");

        var record = NdefParser.ExtractFirstRecord(e.CardData);
        if (record == null || string.IsNullOrEmpty(record.Payload))
        {
            _logService.Warning("Failed to extract NDEF payload from card data");
            return;
        }

        _logService.Info($"NDEF Payload extracted: {record.Payload}");
        _logService.Info($"Record type: {(record.IsUri ? "URI" : "Text/Other")}");

        // Perform actions based on settings
        if (CopyToClipboard)
        {
            _logService.Info("Copying to clipboard...");
            _actionService.CopyToClipboard(record.Payload);
            _logService.Info("Copied to clipboard successfully");
        }

        if (LaunchUrls)
        {
            if (record.IsUri)
            {
                _logService.Info("Attempting to launch URL...");
                _actionService.LaunchUrl(record.Payload);
            }
            else
            {
                _logService.Info("Skipping browser launch - record is not a URI");
            }
        }

        if (TypeAsKeyboard)
        {
            _logService.Info("Typing as keyboard input...");
            _actionService.TypeText(record.Payload, SelectedSuffix.Value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
