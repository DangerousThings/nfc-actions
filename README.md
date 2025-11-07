# NFC Actions

A Windows 11 system tray application that monitors NFC card readers and performs automated actions when NFC tags are detected.

## Features

- **System Tray Application**: Runs in the background with a system tray icon
- **Multi-Reader Support**: Automatically detects and monitors all connected PC/SC-compatible NFC readers
- **Dynamic USB Detection**: Automatically handles USB reader plug/unplug events
- **Reader Selection**: Enable/disable monitoring for specific readers
- **NDEF Payload Extraction**: Reads NDEF data from NFC tags
- **Configurable Actions**:
  - Copy NDEF payload to clipboard
  - Launch URLs in default browser
  - Type NDEF content as keyboard input
- **Persistent Settings**: Remembers reader preferences and action settings between sessions

## Requirements

- Windows 11 (or Windows 10 with PC/SC service)
- .NET 7.0 Runtime or later
- PC/SC-compatible NFC reader

## Building the Application

### Using Visual Studio 2022

1. Open `NfcActions.sln` in Visual Studio 2022
2. Build the solution (Ctrl+Shift+B)
3. Run the application (F5)

### Using .NET CLI

```bash
dotnet build NfcActions.sln
dotnet run --project NfcActions/NfcActions.csproj
```

## Usage

### Starting the Application

When you run NFC Actions, it will:
1. Start minimized to the system tray
2. Show a notification balloon indicating it's running
3. Automatically detect all connected NFC readers
4. Begin monitoring enabled readers for card events

### Accessing the Main Window

- **Double-click** the tray icon, OR
- **Right-click** the tray icon and select "Open"

### Configuring Readers

In the main window:
1. View all detected NFC readers in the "Active Readers" list
2. Check/uncheck readers to enable/disable monitoring
3. Settings are saved automatically

### Configuring Actions

Select which actions to perform when an NFC tag is detected:

- **Copy NDEF data to clipboard**: Copies the NDEF payload text to the clipboard
- **Launch URLs in default browser**: If the NDEF payload is a URL, opens it in your default browser
- **Type NDEF content as keyboard input**: Simulates typing the NDEF payload (useful for form filling)

**Note**: Only the payload from the first NDEF record is used. Tags with multiple messages/records will use the first one.

### Exiting the Application

- Right-click the tray icon and select "Exit"

## Settings

Settings are stored in:
```
%APPDATA%\NfcActions\settings.json
```

The settings file includes:
- List of disabled readers
- Action preferences (clipboard, URLs, keyboard)

## Project Structure

```
NfcActions/
├── Models/
│   ├── AppSettings.cs      # Application settings model
│   └── ReaderItem.cs        # Reader list item model
├── Services/
│   ├── ActionService.cs     # Handles clipboard, browser, and keyboard actions
│   ├── CardReaderService.cs # PC/SC reader monitoring and card detection
│   ├── NdefParser.cs        # NDEF message parsing
│   └── SettingsService.cs   # Settings persistence
├── ViewModels/
│   └── MainViewModel.cs     # Main window view model
├── App.xaml                 # Application resources and startup
├── App.xaml.cs              # Application lifecycle and tray icon
├── MainWindow.xaml          # Main window UI
└── MainWindow.xaml.cs       # Main window code-behind
```

## Dependencies

- **PCSC** (v7.0.1): PC/SC wrapper for smart card communication
- **PCSC.Iso7816** (v7.0.1): ISO 7816 APDU commands
- **InputSimulatorCore** (v1.0.5): Keyboard input simulation
- **System.Management** (v9.0.10): USB device detection (via WMI)

## Technical Details

### NDEF Parsing

The application reads NDEF (NFC Data Exchange Format) messages from NFC tags using standard ISO 7816 APDUs:
- Selects the NDEF Tag Application
- Reads the Capability Container
- Reads the NDEF file
- Parses the NDEF message structure to extract the payload

### Reader Monitoring

The application polls PC/SC readers every 500ms to detect:
- New readers being connected
- Readers being disconnected
- Cards being inserted
- Cards being removed

### Thread Safety

All PC/SC operations are thread-safe and properly synchronized to handle concurrent reader access.

## Troubleshooting

### No Readers Detected

- Ensure your NFC reader is properly connected
- Check that the Windows Smart Card service is running:
  ```
  services.msc → Smart Card
  ```
- Try unplugging and replugging the reader

### Card Not Detected

- Ensure the reader is enabled in the main window
- Try holding the card on the reader for a longer duration
- Some readers require specific card positioning

### Actions Not Working

- **Clipboard**: Ensure no other application has locked the clipboard
- **Browser**: Check that you have a default browser configured
- **Keyboard**: The keyboard simulation requires the target window to have focus

## License

This project is provided as-is for educational and development purposes.

## Contributing

Feel free to submit issues or pull requests for improvements or bug fixes.
