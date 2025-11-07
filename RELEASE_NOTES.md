# NFC Actions v1.0.0 - Initial Release

## Features

### Core Functionality
- **System Tray Application** - Runs quietly in the background, accessible from the system tray
- **Real-time NFC Monitoring** - Automatically detects and monitors all PC/SC compatible NFC readers
- **Dynamic Reader Management** - Handles USB reader plug/unplug events automatically
- **NDEF Payload Extraction** - Supports Type 2 and Type 4 NFC tags with automatic block size detection

### Actions
- **Copy to Clipboard** - Copies NDEF payload data to clipboard for easy pasting
- **Launch URLs** - Opens URI records in your default browser (URL records only)
- **Keyboard Input** - Types NDEF content as keyboard input into active application

### User Interface
- **Configuration Window** - Clean, simple interface for managing readers and actions
- **Real-time Activity Log** - Color-coded logging (Debug, Info, Warning, Error) for visibility
- **Dangerous Things Branding** - Custom icons and clickable logo

### Technical Features
- **Settings Persistence** - Remembers your preferences between sessions
- **Auto-start on Login** - Automatically starts when you log in to Windows
- **File Logging** - Debug logs saved to application directory for troubleshooting
- **Single-file Deployment** - No .NET runtime installation required

## Installation

1. Download **NfcActions-Setup.msi** from the release assets
2. Run the installer
3. The application will:
   - Install to `%LOCALAPPDATA%\DangerousThings\NFC Actions`
   - Create a Start Menu shortcut
   - Configure automatic startup on login
   - Start running immediately in the system tray

## System Requirements

- **Operating System**: Windows 10 or Windows 11
- **Hardware**: PC/SC compatible NFC reader (USB or built-in)
- **Runtime**: None required (self-contained)

## Tested Readers

- Identiv uTrust 3700 F
- HID OMNIKEY 5022 CL

## Usage

1. **First Launch**: Click the tray icon to open the configuration window
2. **Enable/Disable Readers**: Check or uncheck readers in the "Active Readers" list
3. **Configure Actions**: Select which actions to perform when a card is detected
4. **Tap NFC Card**: Simply tap your NFC card to any enabled reader
5. **Monitor Activity**: Watch the activity log for real-time feedback

## Known Limitations

- Only the first NDEF record is processed (multiple records not supported yet)
- Only NDEF payload is used (not the full NDEF message structure)
- URI detection is limited to standard URI record types and identifier codes

## Support

- **Website**: https://dangerousthings.com
- **Repository**: https://git.dngr.us/DangerousThings/nfc-actions
- **Issues**: Report bugs via the repository issue tracker

## License

Copyright © 2025 Dangerous Things

---

**Built with Claude Code** - https://claude.com/claude-code
