using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PCSC;

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

            var result = context.GetStatusChange(0, readerStates);

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

    private byte[]? ReadCardData(string readerName)
    {
        try
        {
            _logService?.Debug($"--- Starting card read from {readerName} ---");

            using var context = ContextFactory.Instance.Establish(SCardScope.System);

            _logService?.Debug("Connecting to reader...");
            using var reader = context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any);

            _logService?.Debug($"Connected. Active protocol: {reader.Protocol}");

            // Get ATR (Answer To Reset)
            var atr = reader.GetAttrib(SCardAttribute.AtrString);
            if (atr != null && atr.Length > 0)
            {
                _logService?.Debug($"ATR: {BitConverter.ToString(atr).Replace("-", " ")}");
            }

            byte[]? ndefData = null;

            // Strategy 1: Try Type 4 Tag (ISO 14443-4 / ISO-DEP)
            _logService?.Debug("=== Attempting Type 4 Tag NDEF read ===");
            ndefData = TryReadType4Tag(reader);
            if (ndefData != null && ndefData.Length > 0)
            {
                _logService?.Info("Successfully read NDEF data using Type 4 method");
                return ndefData;
            }

            // Strategy 2: Try direct NDEF file read
            _logService?.Debug("=== Attempting direct NDEF file read ===");
            ndefData = TryReadNdefDirect(reader);
            if (ndefData != null && ndefData.Length > 0)
            {
                _logService?.Info("Successfully read NDEF data using direct method");
                return ndefData;
            }

            // Strategy 3: Try reading raw tag memory (Type 2)
            _logService?.Debug("=== Attempting Type 2 Tag read ===");
            ndefData = TryReadType2Tag(reader);
            if (ndefData != null && ndefData.Length > 0)
            {
                _logService?.Info("Successfully read data using Type 2 method");
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
