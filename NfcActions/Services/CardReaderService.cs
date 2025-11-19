using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PCSC;
using PCSC.Exceptions;

namespace NfcActions.Services;

public class CardReaderService : IDisposable
{
    private readonly Timer _pollTimer;
    private readonly Dictionary<string, bool> _readerCardPresent = new();
    private readonly HashSet<string> _disabledReaders = new();
    private readonly SynchronizationContext? _syncContext;
    private readonly object _lock = new();
    private readonly LogService? _logService;

    private const int POLL_INTERVAL_MS = 500;
    private const int MAX_RETRY_ATTEMPTS = 3;
    private const int INITIAL_RETRY_DELAY_MS = 100;
    private const int STATUS_TIMEOUT_MS = 1000;

    public event EventHandler<ReaderEventArgs>? ReaderAdded;
    public event EventHandler<ReaderEventArgs>? ReaderRemoved;
    public event EventHandler<CardEventArgs>? CardInserted;
    public event EventHandler<CardEventArgs>? CardRemoved;

    public CardReaderService(LogService? logService = null)
    {
        _syncContext = SynchronizationContext.Current;
        _pollTimer = new Timer(PollReaders, null, Timeout.Infinite, Timeout.Infinite);
        _logService = logService;
    }

    public void Start()
    {
        _logService?.Info("Starting CardReaderService...");

        // Initialize with current readers
        RefreshReaders();

        // Start polling
        _pollTimer.Change(POLL_INTERVAL_MS, POLL_INTERVAL_MS);
        _logService?.Info("CardReaderService started successfully");
    }

    private void PollReaders(object? state)
    {
        try
        {
            RefreshReaders();
            MonitorCardStates();
        }
        catch (Exception)
        {
            // Error during polling, continue anyway
        }
    }

    private void RefreshReaders()
    {
        lock (_lock)
        {
            try
            {
                using var context = ContextFactory.Instance.Establish(SCardScope.System);
                var currentReaders = context.GetReaders()?.ToList() ?? new List<string>();

                // Find removed readers
                var removedReaders = _readerCardPresent.Keys.Except(currentReaders).ToList();
                foreach (var reader in removedReaders)
                {
                    _readerCardPresent.Remove(reader);
                    _logService?.Info($"Reader removed: {reader}");
                    RaiseEvent(() => ReaderRemoved?.Invoke(this, new ReaderEventArgs(reader)));
                }

                // Find new readers
                var newReaders = currentReaders.Except(_readerCardPresent.Keys).ToList();
                foreach (var reader in newReaders)
                {
                    _readerCardPresent[reader] = false;
                    _logService?.Info($"Reader added: {reader}");
                    RaiseEvent(() => ReaderAdded?.Invoke(this, new ReaderEventArgs(reader)));
                }
            }
            catch (Exception ex)
            {
                _logService?.Error($"Error refreshing readers: {ex.Message}");
            }
        }
    }

    private void MonitorCardStates()
    {
        lock (_lock)
        {
            foreach (var readerName in _readerCardPresent.Keys.ToList())
            {
                // Skip disabled readers
                if (_disabledReaders.Contains(readerName))
                    continue;

                try
                {
                    var isPresent = IsCardPresent(readerName);
                    var wasPresent = _readerCardPresent[readerName];

                    if (isPresent && !wasPresent)
                    {
                        _readerCardPresent[readerName] = true;
                        _logService?.Info($"Card inserted on reader: {readerName}");
                        var cardData = ReadCardData(readerName);

                        if (cardData != null && cardData.Length > 0)
                        {
                            _logService?.Debug($"Read {cardData.Length} bytes from card");
                            _logService?.Debug($"Card data (hex): {BitConverter.ToString(cardData).Replace("-", " ")}");
                        }
                        else
                        {
                            _logService?.Warning("No data read from card");
                        }

                        RaiseEvent(() => CardInserted?.Invoke(this, new CardEventArgs(readerName, cardData)));
                    }
                    else if (!isPresent && wasPresent)
                    {
                        _readerCardPresent[readerName] = false;
                        _logService?.Info($"Card removed from reader: {readerName}");
                        RaiseEvent(() => CardRemoved?.Invoke(this, new CardEventArgs(readerName, null)));
                    }
                }
                catch (Exception ex)
                {
                    _logService?.Error($"Error monitoring reader {readerName}: {ex.Message}");
                }
            }
        }
    }

    private bool IsCardPresent(string readerName)
    {
        try
        {
            using var context = ContextFactory.Instance.Establish(SCardScope.System);

            var readerStates = new[]
            {
                new SCardReaderState
                {
                    ReaderName = readerName
                }
            };

            var result = context.GetStatusChange(STATUS_TIMEOUT_MS, readerStates);

            if (result == SCardError.Success && readerStates.Length > 0)
            {
                var state = readerStates[0].EventState;
                return (state & SCRState.Present) == SCRState.Present;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public List<string> GetAvailableReaders()
    {
        try
        {
            using var context = ContextFactory.Instance.Establish(SCardScope.System);
            return context.GetReaders()?.ToList() ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public void EnableReader(string readerName)
    {
        lock (_lock)
        {
            _disabledReaders.Remove(readerName);
        }
    }

    public void DisableReader(string readerName)
    {
        lock (_lock)
        {
            _disabledReaders.Add(readerName);
        }
    }

    public bool IsReaderEnabled(string readerName)
    {
        lock (_lock)
        {
            return !_disabledReaders.Contains(readerName);
        }
    }

    public void SetDisabledReaders(IEnumerable<string> disabledReaders)
    {
        lock (_lock)
        {
            _disabledReaders.Clear();
            foreach (var reader in disabledReaders)
            {
                _disabledReaders.Add(reader);
            }
        }
    }

    private ICardReader? ConnectReaderWithRetry(ISCardContext context, string readerName)
    {
        int retryDelay = INITIAL_RETRY_DELAY_MS;

        for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
        {
            try
            {
                _logService?.Debug($"Attempting to connect to reader (attempt {attempt}/{MAX_RETRY_ATTEMPTS})...");

                // Try exclusive mode first
                try
                {
                    var reader = context.ConnectReader(readerName, SCardShareMode.Exclusive, SCardProtocol.Any);
                    _logService?.Debug($"Successfully connected with exclusive access on attempt {attempt}");
                    return reader;
                }
                catch (PCSCException ex) when (ex.SCardError == SCardError.SharingViolation && attempt < MAX_RETRY_ATTEMPTS)
                {
                    _logService?.Debug($"Exclusive access denied (sharing violation), retrying in {retryDelay}ms...");
                    Thread.Sleep(retryDelay);
                    retryDelay *= 2; // Exponential backoff
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logService?.Warning($"Connection attempt {attempt} failed: {ex.Message}");

                if (attempt == MAX_RETRY_ATTEMPTS)
                {
                    // On final attempt, try shared mode as fallback
                    try
                    {
                        _logService?.Warning("All exclusive access attempts failed, falling back to shared mode...");
                        var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any);
                        _logService?.Warning("Connected with shared access (fallback)");
                        return reader;
                    }
                    catch (Exception fallbackEx)
                    {
                        _logService?.Error($"Failed to connect even with shared mode: {fallbackEx.Message}");
                        throw;
                    }
                }

                Thread.Sleep(retryDelay);
                retryDelay *= 2; // Exponential backoff
            }
        }

        return null;
    }

    private enum NfcCardType
    {
        Unknown,
        Type2,    // NTAG, MIFARE Ultralight
        Type4     // ISO-DEP, DESFire
    }

    private NfcCardType DetectCardType(ICardReader reader, byte[]? atr)
    {
        try
        {
            // Check ATR for Type 4 indicators
            if (atr != null && atr.Length > 0)
            {
                // Check for ISO 14443-4 support in historical bytes
                _logService?.Debug($"Analyzing ATR for card type detection: {BitConverter.ToString(atr).Replace("-", " ")}");

                // Type 4 cards typically have longer ATRs with historical bytes
                if (atr.Length > 10)
                {
                    _logService?.Debug("Long ATR detected, likely Type 4 card");
                    return NfcCardType.Type4;
                }
            }

            // Try to detect card type using protocol
            if (reader.Protocol == SCardProtocol.T0 || reader.Protocol == SCardProtocol.T1)
            {
                _logService?.Debug($"T=0/T=1 protocol detected, likely Type 4 card");
                // T=0 or T=1 protocols typically indicate Type 4 cards
            }

            // Try a simple Type 2 read command
            var testRead = new byte[] { 0x30, 0x00 }; // READ block 0
            var response = TransmitApdu(reader, testRead, "Type 2 detection probe");
            if (response != null && response.Length >= 16)
            {
                _logService?.Debug("Type 2 READ command successful");
                return NfcCardType.Type2;
            }

            // Default to Type 4 for ISO-DEP cards
            _logService?.Debug("Defaulting to Type 4 card");
            return NfcCardType.Type4;
        }
        catch (Exception ex)
        {
            _logService?.Debug($"Card type detection failed: {ex.Message}");
            return NfcCardType.Unknown;
        }
    }

    private byte[]? ReadCardData(string readerName)
    {
        try
        {
            _logService?.Debug($"--- Starting card read from {readerName} ---");

            using var context = ContextFactory.Instance.Establish(SCardScope.System);

            _logService?.Debug("Connecting to reader...");
            using var reader = ConnectReaderWithRetry(context, readerName);

            if (reader == null)
            {
                _logService?.Error("Failed to connect to reader after all retry attempts");
                return null;
            }

            _logService?.Debug($"Connected. Active protocol: {reader.Protocol}");

            // Get ATR (Answer To Reset)
            var atr = reader.GetAttrib(SCardAttribute.AtrString);
            if (atr != null && atr.Length > 0)
            {
                _logService?.Debug($"ATR: {BitConverter.ToString(atr).Replace("-", " ")}");
            }

            // Detect card type
            var cardType = DetectCardType(reader, atr);
            _logService?.Info($"Detected card type: {cardType}");

            byte[]? ndefData = null;

            // Use appropriate strategy based on detected card type
            switch (cardType)
            {
                case NfcCardType.Type2:
                    _logService?.Debug("=== Reading Type 2 Tag ===");
                    ndefData = TryReadType2TagEnhanced(reader);
                    if (ndefData != null && ndefData.Length > 0)
                    {
                        _logService?.Info("Successfully read NDEF data from Type 2 tag");
                        return ndefData;
                    }
                    // Fallback to Type 4 if Type 2 fails
                    _logService?.Debug("Type 2 read failed, trying Type 4 as fallback");
                    ndefData = TryReadType4TagEnhanced(reader);
                    break;

                case NfcCardType.Type4:
                    _logService?.Debug("=== Reading Type 4 Tag ===");
                    ndefData = TryReadType4TagEnhanced(reader);
                    if (ndefData != null && ndefData.Length > 0)
                    {
                        _logService?.Info("Successfully read NDEF data from Type 4 tag");
                        return ndefData;
                    }
                    // Fallback to Type 2 if Type 4 fails
                    _logService?.Debug("Type 4 read failed, trying Type 2 as fallback");
                    ndefData = TryReadType2TagEnhanced(reader);
                    break;

                default:
                    // Try both methods if unknown
                    _logService?.Debug("Unknown card type, trying all methods");
                    ndefData = TryReadType4TagEnhanced(reader);
                    if (ndefData == null || ndefData.Length == 0)
                    {
                        ndefData = TryReadType2TagEnhanced(reader);
                    }
                    break;
            }

            if (ndefData != null && ndefData.Length > 0)
            {
                _logService?.Info($"Successfully read {ndefData.Length} bytes of NDEF data");
                return ndefData;
            }

            _logService?.Warning("All read strategies failed - no NDEF data retrieved");
            return null;
        }
        catch (Exception ex)
        {
            _logService?.Error($"Exception in ReadCardData: {ex.Message}");
            _logService?.Debug($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    private byte[]? TryReadType4TagEnhanced(ICardReader reader)
    {
        try
        {
            // Step 1: Select NDEF Tag Application (AID: D2760000850101)
            var selectNdef = new byte[] { 0x00, 0xA4, 0x04, 0x00, 0x07, 0xD2, 0x76, 0x00, 0x00, 0x85, 0x01, 0x01, 0x00 };
            var response = TransmitApdu(reader, selectNdef, "Select NDEF Application");

            if (!IsSuccess(response))
            {
                _logService?.Debug("NDEF application not found");
                return null;
            }

            // Step 2: Select Capability Container (CC) file
            var selectCC = new byte[] { 0x00, 0xA4, 0x00, 0x0C, 0x02, 0xE1, 0x03 };
            response = TransmitApdu(reader, selectCC, "Select CC File");

            if (!IsSuccess(response))
            {
                _logService?.Debug("Failed to select CC file");
                return null;
            }

            // Step 3: Read CC to get NDEF file info
            var readCC = new byte[] { 0x00, 0xB0, 0x00, 0x00, 0x0F };
            response = TransmitApdu(reader, readCC, "Read CC");

            if (!IsSuccess(response) || response?.Length < 17)
            {
                _logService?.Debug("Failed to read CC");
                return null;
            }

            // Parse CC to get max NDEF size
            var ccData = new byte[15];
            if (response != null)
            {
                Array.Copy(response, ccData, Math.Min(15, response.Length - 2));
            }

            // CC format: 2 bytes length, 1 byte version, 2 bytes MLe, 2 bytes MLc, then TLV
            var maxNdefSize = (ccData[3] << 8) | ccData[4]; // MLe (Maximum Length for data read)
            var maxApduSize = (ccData[5] << 8) | ccData[6]; // MLc (Maximum Length for command)
            _logService?.Debug($"CC: Max NDEF size = {maxNdefSize}, Max APDU size = {maxApduSize}");

            // Step 4: Select NDEF file
            var selectNdefFile = new byte[] { 0x00, 0xA4, 0x00, 0x0C, 0x02, 0xE1, 0x04 };
            response = TransmitApdu(reader, selectNdefFile, "Select NDEF File");

            if (!IsSuccess(response))
            {
                _logService?.Debug("Failed to select NDEF file");
                return null;
            }

            // Step 5: Read NDEF length (always 2 bytes for Type 4 tags per NFC Forum spec)
            var readLength = new byte[] { 0x00, 0xB0, 0x00, 0x00, 0x02 }; // Read 2 bytes
            response = TransmitApdu(reader, readLength, "Read NDEF Length");

            if (response == null || response.Length < 4)
            {
                _logService?.Debug("Failed to read NDEF length");
                return null;
            }

            // Type 4 tags use 2-byte NLEN field (big-endian)
            int ndefLength = (response[0] << 8) | response[1];
            int dataOffset = 2; // NDEF message starts at byte 2

            _logService?.Debug($"NDEF length: {ndefLength} bytes (0x{response[0]:X2}{response[1]:X2})");

            if (ndefLength == 0)
            {
                _logService?.Warning("NDEF length is 0 - empty tag");
                return null;
            }

            if (ndefLength > 65535)
            {
                _logService?.Warning($"NDEF length too large: {ndefLength}");
                return null;
            }

            // Step 6: Read NDEF data in chunks
            var allData = new List<byte>();
            var offset = dataOffset;
            var maxReadSize = Math.Min(maxApduSize > 0 ? maxApduSize : 250, 250); // Use CC info or default to 250

            while (allData.Count < ndefLength)
            {
                var remainingBytes = ndefLength - allData.Count;
                var readSize = Math.Min(remainingBytes, maxReadSize);

                var readData = new byte[] {
                    0x00, 0xB0,
                    (byte)(offset >> 8), (byte)(offset & 0xFF),
                    (byte)readSize
                };

                response = TransmitApdu(reader, readData, $"Read NDEF chunk at offset {offset}");

                if (!IsSuccess(response))
                {
                    _logService?.Error($"Failed to read NDEF data at offset {offset}");
                    break;
                }

                var dataLength = response?.Length - 2 ?? 0;
                if (dataLength > 0 && response != null)
                {
                    for (int i = 0; i < dataLength && allData.Count < ndefLength; i++)
                    {
                        allData.Add(response[i]);
                    }
                    offset += dataLength;
                }
                else
                {
                    _logService?.Warning("No data received in chunk read");
                    break;
                }

                _logService?.Debug($"Read {dataLength} bytes, total: {allData.Count}/{ndefLength}");
            }

            if (allData.Count > 0)
            {
                _logService?.Info($"Successfully read {allData.Count} bytes from Type 4 tag");
                return allData.ToArray();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logService?.Debug($"Type 4 enhanced read exception: {ex.Message}");
            return null;
        }
    }

    private byte[]? TryReadType2TagEnhanced(ICardReader reader)
    {
        try
        {
            _logService?.Debug("Starting enhanced Type 2 tag read");

            // Step 1: Read first 16 bytes (blocks 0-3) to identify tag
            var readHeader = new byte[] { 0x30, 0x00 }; // READ from block 0
            var response = TransmitApdu(reader, readHeader, "Read header blocks 0-3");

            if (response == null || response.Length < 16)
            {
                _logService?.Debug("Failed to read header blocks");
                return null;
            }

            // Step 2: Check Capability Container (CC) at block 3 (bytes 12-15)
            // CC format: Magic number, Version, Memory size, Read/Write access
            if (response.Length >= 16)
            {
                var cc0 = response[12]; // Should be 0xE1 for NDEF
                var cc1 = response[13]; // Version
                var cc2 = response[14]; // Memory size
                var cc3 = response[15]; // Read/Write access

                _logService?.Debug($"CC: {cc0:X2} {cc1:X2} {cc2:X2} {cc3:X2}");

                if (cc0 != 0xE1)
                {
                    _logService?.Debug("Not an NDEF formatted tag (CC0 != 0xE1)");
                    // Continue anyway as some tags might still have NDEF data
                }

                var memorySize = (cc2 & 0xFF) * 8;
                _logService?.Debug($"Tag memory size: {memorySize} bytes");
            }

            // Step 3: Read NDEF data starting from block 4
            var allData = new List<byte>();
            byte currentBlock = 4;
            int tlvPosition = 0;
            bool ndefFound = false;
            int ndefLength = 0;

            // Read up to 64 blocks (256 bytes) or until terminator
            while (currentBlock < 64)
            {
                var readBlock = new byte[] { 0x30, currentBlock };
                response = TransmitApdu(reader, readBlock, $"Read block {currentBlock}");

                if (response == null || response.Length < 16)
                {
                    _logService?.Debug($"Failed to read block {currentBlock}");
                    break;
                }

                // Process 4 blocks (16 bytes) at a time
                for (int i = 0; i < 16 && (currentBlock * 4 + i / 4) < 256; i++)
                {
                    allData.Add(response[i]);

                    // Parse TLV structure to find NDEF message
                    if (!ndefFound && allData.Count >= 2)
                    {
                        // Check for NDEF TLV (0x03)
                        if (allData[tlvPosition] == 0x03)
                        {
                            ndefFound = true;
                            _logService?.Debug($"Found NDEF TLV at position {tlvPosition}");

                            // Parse length
                            if (allData.Count > tlvPosition + 1)
                            {
                                var lengthByte = allData[tlvPosition + 1];
                                if (lengthByte == 0xFF && allData.Count > tlvPosition + 3)
                                {
                                    // 3-byte length
                                    ndefLength = (allData[tlvPosition + 2] << 8) | allData[tlvPosition + 3];
                                    tlvPosition += 4; // Skip TLV header
                                }
                                else
                                {
                                    // 1-byte length
                                    ndefLength = lengthByte;
                                    tlvPosition += 2; // Skip TLV header
                                }
                                _logService?.Debug($"NDEF length: {ndefLength} bytes");
                            }
                        }
                        else if (allData[tlvPosition] == 0xFE)
                        {
                            // Terminator TLV
                            _logService?.Debug("Found terminator TLV");
                            break;
                        }
                        else if (allData[tlvPosition] == 0x00)
                        {
                            // NULL TLV, skip
                            tlvPosition++;
                        }
                        else
                        {
                            // Other TLV, skip based on length
                            if (allData.Count > tlvPosition + 1)
                            {
                                var len = allData[tlvPosition + 1];
                                tlvPosition += 2 + len;
                            }
                        }
                    }
                }

                // Check if we've read enough NDEF data
                if (ndefFound && allData.Count >= tlvPosition + ndefLength)
                {
                    _logService?.Debug("Read complete NDEF message");
                    break;
                }

                currentBlock += 4; // Move to next set of 4 blocks
            }

            // Extract NDEF data if found
            if (ndefFound && ndefLength > 0 && allData.Count >= tlvPosition + ndefLength)
            {
                var ndefData = new byte[ndefLength];
                for (int i = 0; i < ndefLength; i++)
                {
                    ndefData[i] = allData[tlvPosition + i];
                }
                _logService?.Info($"Successfully extracted {ndefLength} bytes of NDEF data from Type 2 tag");
                return ndefData;
            }

            // If no NDEF TLV found but we have data, return raw data
            if (allData.Count > 0)
            {
                _logService?.Warning("No NDEF TLV found, returning raw data");
                return allData.ToArray();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logService?.Debug($"Type 2 enhanced read exception: {ex.Message}");
            return null;
        }
    }

    private byte[]? TryReadType4Tag(ICardReader reader)
    {
        try
        {
            // Select NDEF Tag Application (AID: D2760000850101)
            var selectNdef = new byte[] { 0x00, 0xA4, 0x04, 0x00, 0x07, 0xD2, 0x76, 0x00, 0x00, 0x85, 0x01, 0x01, 0x00 };
            var response = TransmitApdu(reader, selectNdef, "Select NDEF Application");

            if (!IsSuccess(response))
            {
                _logService?.Debug("NDEF application not found (this is normal for non-Type 4 tags)");
                return null;
            }

            // Select Capability Container file
            var selectCC = new byte[] { 0x00, 0xA4, 0x00, 0x0C, 0x02, 0xE1, 0x03 };
            response = TransmitApdu(reader, selectCC, "Select CC File");

            if (IsSuccess(response))
            {
                // Read CC
                var readCC = new byte[] { 0x00, 0xB0, 0x00, 0x00, 0x0F };
                response = TransmitApdu(reader, readCC, "Read CC");
            }

            // Select NDEF file
            var selectNdefFile = new byte[] { 0x00, 0xA4, 0x00, 0x0C, 0x02, 0xE1, 0x04 };
            response = TransmitApdu(reader, selectNdefFile, "Select NDEF File");

            if (!IsSuccess(response))
            {
                return null;
            }

            // Read NDEF length (first 2 bytes)
            var readLength = new byte[] { 0x00, 0xB0, 0x00, 0x00, 0x02 };
            response = TransmitApdu(reader, readLength, "Read NDEF Length");

            if (response == null || response.Length < 4)
            {
                return null;
            }

            int ndefLength = (response[0] << 8) | response[1];
            _logService?.Debug($"NDEF message length: {ndefLength} bytes");

            if (ndefLength == 0 || ndefLength > 8192)
            {
                _logService?.Warning($"Invalid NDEF length: {ndefLength}");
                return null;
            }

            // Read actual NDEF data
            var readNdef = new byte[] { 0x00, 0xB0, 0x00, 0x02, (byte)Math.Min(ndefLength, 250) };
            response = TransmitApdu(reader, readNdef, "Read NDEF Data");

            if (response != null && response.Length > 2)
            {
                var data = new byte[response.Length - 2];
                Array.Copy(response, data, data.Length);
                return data;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logService?.Debug($"Type 4 read exception: {ex.Message}");
            return null;
        }
    }

    private byte[]? TryReadNdefDirect(ICardReader reader)
    {
        try
        {
            // Try reading from common NDEF file locations
            var selectFile = new byte[] { 0x00, 0xA4, 0x00, 0x0C, 0x02, 0xE1, 0x04 };
            var response = TransmitApdu(reader, selectFile, "Direct select NDEF");

            if (IsSuccess(response))
            {
                var readData = new byte[] { 0x00, 0xB0, 0x00, 0x00, 0xF0 };
                response = TransmitApdu(reader, readData, "Direct read data");

                if (response != null && response.Length > 2)
                {
                    var data = new byte[response.Length - 2];
                    Array.Copy(response, data, data.Length);
                    return data;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logService?.Debug($"Direct read exception: {ex.Message}");
            return null;
        }
    }

    private byte[]? TryReadType2Tag(ICardReader reader)
    {
        try
        {
            // Type 2 tags use direct memory read commands
            // Read blocks starting from block 4 (where NDEF usually starts)
            _logService?.Debug("Attempting Type 2 tag read (direct memory access)");

            var allData = new List<byte>();
            int blockSize = 4; // Default NTAG/MIFARE Ultralight block size
            bool blockSizeDetected = false;

            // Read first few blocks to get NDEF length
            for (byte block = 4; block < 64;)
            {
                var readBlock = new byte[] { 0xFF, 0xB0, 0x00, block, 0x10 };
                var response = TransmitApdu(reader, readBlock, $"Read block {block}");

                if (response == null || response.Length < 2)
                {
                    break;
                }

                if (!IsSuccess(response))
                {
                    // Try alternative command
                    readBlock = new byte[] { 0x30, block };
                    response = TransmitApdu(reader, readBlock, $"Read block {block} (alt)");

                    if (response == null || !IsSuccess(response))
                    {
                        break;
                    }
                }

                // Detect block size from first successful read
                var dataLength = response.Length - 2; // Exclude SW1 SW2
                if (!blockSizeDetected && dataLength > 0)
                {
                    blockSize = dataLength;
                    blockSizeDetected = true;
                    _logService?.Debug($"Detected block size: {blockSize} bytes");
                }

                // Add data (excluding status words)
                if (dataLength > 0)
                {
                    for (int i = 0; i < dataLength; i++)
                    {
                        allData.Add(response[i]);
                    }
                }

                // Stop if we've hit terminator TLV
                if (allData.Count > 0 && allData[allData.Count - 1] == 0xFE)
                {
                    _logService?.Debug("Found NDEF terminator TLV (0xFE)");
                    break;
                }

                if (allData.Count > 200)
                {
                    _logService?.Debug("Read limit reached (200 bytes)");
                    break;
                }

                // Advance block pointer based on detected block size
                // Type 2 tags have 4-byte blocks, so if we got 16 bytes, we read 4 blocks
                block += (byte)(blockSize / 4);
            }

            if (allData.Count > 0)
            {
                _logService?.Debug($"Read {allData.Count} bytes from Type 2 tag");
                return allData.ToArray();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logService?.Debug($"Type 2 read exception: {ex.Message}");
            return null;
        }
    }

    private byte[]? TransmitApdu(ICardReader reader, byte[] apdu, string description)
    {
        try
        {
            _logService?.Debug($"TX [{description}]: {BitConverter.ToString(apdu).Replace("-", " ")}");

            var response = new byte[256];
            var receivedLength = reader.Transmit(apdu, response);

            if (receivedLength > 0)
            {
                var result = new byte[receivedLength];
                Array.Copy(response, result, receivedLength);

                _logService?.Debug($"RX [{description}]: {BitConverter.ToString(result).Replace("-", " ")}");

                return result;
            }

            _logService?.Debug($"RX [{description}]: No data received");
            return null;
        }
        catch (Exception ex)
        {
            _logService?.Debug($"TX/RX [{description}] Exception: {ex.Message}");
            return null;
        }
    }

    private bool IsSuccess(byte[]? response)
    {
        if (response == null || response.Length < 2)
            return false;

        var sw1 = response[response.Length - 2];
        var sw2 = response[response.Length - 1];

        return (sw1 == 0x90 && sw2 == 0x00) || sw1 == 0x91;
    }

    private void RaiseEvent(Action action)
    {
        if (_syncContext != null)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }
}

public class ReaderEventArgs : EventArgs
{
    public string ReaderName { get; }

    public ReaderEventArgs(string readerName)
    {
        ReaderName = readerName;
    }
}

public class CardEventArgs : EventArgs
{
    public string ReaderName { get; }
    public byte[]? CardData { get; }

    public CardEventArgs(string readerName, byte[]? cardData)
    {
        ReaderName = readerName;
        CardData = cardData;
    }
}
