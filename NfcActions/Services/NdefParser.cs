using System;
using System.Text;

namespace NfcActions.Services;

public class NdefRecord
{
    public string Payload { get; set; } = string.Empty;
    public bool IsUri { get; set; }
}

public static class NdefParser
{
    /// <summary>
    /// Extracts the first NDEF payload from raw card data.
    /// Returns the payload as a string, or null if no valid NDEF message found.
    /// </summary>
    public static string? ExtractFirstPayload(byte[] data)
    {
        var record = ExtractFirstRecord(data);
        return record?.Payload;
    }

    /// <summary>
    /// Extracts the first NDEF record from raw card data with type information.
    /// Returns an NdefRecord object, or null if no valid NDEF message found.
    /// </summary>
    public static NdefRecord? ExtractFirstRecord(byte[] data)
    {
        if (data == null || data.Length < 3)
            return null;

        try
        {
            int position = 0;

            // Look for NDEF TLV (Type-Length-Value) structure
            // We're looking for TLV type 0x03 which indicates NDEF Message
            while (position < data.Length - 2)
            {
                byte tlvType = data[position];

                if (tlvType == 0x00) // NULL TLV, skip
                {
                    position++;
                    continue;
                }

                if (tlvType == 0xFE) // Terminator TLV
                {
                    break;
                }

                position++; // Move to length byte

                if (position >= data.Length)
                    break;

                int length;
                if (data[position] == 0xFF) // 3-byte length format
                {
                    if (position + 2 >= data.Length)
                        break;
                    length = (data[position + 1] << 8) | data[position + 2];
                    position += 3;
                }
                else
                {
                    length = data[position];
                    position++;
                }

                if (tlvType == 0x03) // NDEF Message TLV
                {
                    if (position + length > data.Length)
                        break;

                    // Parse NDEF message
                    var record = ParseNdefMessage(data, position, length);
                    if (record != null)
                        return record;
                }

                position += length;
            }

            // If we didn't find TLV structure, try to parse as raw NDEF message
            return ParseNdefMessage(data, 0, data.Length);
        }
        catch
        {
            return null;
        }
    }

    private static NdefRecord? ParseNdefMessage(byte[] data, int offset, int length)
    {
        if (offset + length > data.Length || length < 3)
            return null;

        int position = offset;
        int endPosition = offset + length;

        while (position < endPosition)
        {
            if (position >= data.Length)
                break;

            byte header = data[position];
            position++;

            bool mb = (header & 0x80) != 0;  // Message Begin
            bool me = (header & 0x40) != 0;  // Message End
            bool cf = (header & 0x20) != 0;  // Chunk Flag
            bool sr = (header & 0x10) != 0;  // Short Record
            bool il = (header & 0x08) != 0;  // ID Length present
            byte tnf = (byte)(header & 0x07); // Type Name Format

            if (position >= endPosition)
                break;

            int typeLength = data[position];
            position++;

            int typePosition = 0; // Track where type field starts

            if (position >= endPosition)
                break;

            int payloadLength;
            if (sr) // Short record - 1 byte payload length
            {
                payloadLength = data[position];
                position++;
            }
            else // Normal record - 4 byte payload length
            {
                if (position + 3 >= endPosition)
                    break;

                payloadLength = (data[position] << 24) |
                               (data[position + 1] << 16) |
                               (data[position + 2] << 8) |
                               data[position + 3];
                position += 4;
            }

            int idLength = 0;
            if (il)
            {
                if (position >= endPosition)
                    break;
                idLength = data[position];
                position++;
            }

            // Remember type position before skipping
            typePosition = position;

            // Read type field to check if it's a URI record
            byte[]? typeField = null;
            if (typeLength > 0 && position + typeLength <= endPosition)
            {
                typeField = new byte[typeLength];
                Array.Copy(data, position, typeField, 0, typeLength);
            }

            // Skip type
            position += typeLength;

            // Skip ID
            position += idLength;

            if (position + payloadLength > endPosition)
                break;

            // Extract payload
            if (payloadLength > 0)
            {
                byte[] payload = new byte[payloadLength];
                Array.Copy(data, position, payload, 0, payloadLength);

                // Determine if this is a URI record
                bool isUri = IsUriRecord(tnf, typeField, payload);

                // Decode payload
                string payloadText = DecodeTextPayload(payload);

                return new NdefRecord
                {
                    Payload = payloadText,
                    IsUri = isUri
                };
            }

            position += payloadLength;

            // If this was the first record, return what we found (or null)
            break;
        }

        return null;
    }

    private static bool IsUriRecord(byte tnf, byte[]? typeField, byte[] payload)
    {
        // TNF Well-Known (0x01) with type "U" is a URI record
        if (tnf == 0x01 && typeField != null && typeField.Length == 1 && typeField[0] == 0x55) // 'U'
        {
            return true;
        }

        // TNF Absolute URI (0x03)
        if (tnf == 0x03)
        {
            return true;
        }

        // Check if payload starts with URI identifier code (0x00-0x23)
        if (payload.Length > 0 && payload[0] <= 0x23)
        {
            string prefix = GetUriPrefix(payload[0]);
            // If we have a recognized URI prefix, it's likely a URI
            if (!string.IsNullOrEmpty(prefix) || payload[0] == 0x00)
            {
                return true;
            }
        }

        return false;
    }

    private static string DecodeTextPayload(byte[] payload)
    {
        if (payload.Length == 0)
            return string.Empty;

        // Check if first byte indicates URI identifier code
        if (payload[0] <= 0x23) // URI identifier codes range from 0x00 to 0x23
        {
            string prefix = GetUriPrefix(payload[0]);
            string uri = Encoding.UTF8.GetString(payload, 1, payload.Length - 1);
            return prefix + uri;
        }

        // Check if it's a text record (first byte is status byte)
        if (payload.Length > 1)
        {
            byte statusByte = payload[0];
            bool isUtf16 = (statusByte & 0x80) != 0;
            int languageCodeLength = statusByte & 0x3F;

            if (languageCodeLength < payload.Length)
            {
                int textStart = 1 + languageCodeLength;
                if (textStart < payload.Length)
                {
                    var encoding = isUtf16 ? Encoding.Unicode : Encoding.UTF8;
                    return encoding.GetString(payload, textStart, payload.Length - textStart);
                }
            }
        }

        // Default: try UTF8 decoding of entire payload
        return Encoding.UTF8.GetString(payload);
    }

    private static string GetUriPrefix(byte code)
    {
        return code switch
        {
            0x00 => "",
            0x01 => "http://www.",
            0x02 => "https://www.",
            0x03 => "http://",
            0x04 => "https://",
            0x05 => "tel:",
            0x06 => "mailto:",
            0x07 => "ftp://anonymous:anonymous@",
            0x08 => "ftp://ftp.",
            0x09 => "ftps://",
            0x0A => "sftp://",
            0x0B => "smb://",
            0x0C => "nfs://",
            0x0D => "ftp://",
            0x0E => "dav://",
            0x0F => "news:",
            0x10 => "telnet://",
            0x11 => "imap:",
            0x12 => "rtsp://",
            0x13 => "urn:",
            0x14 => "pop:",
            0x15 => "sip:",
            0x16 => "sips:",
            0x17 => "tftp:",
            0x18 => "btspp://",
            0x19 => "btl2cap://",
            0x1A => "btgoep://",
            0x1B => "tcpobex://",
            0x1C => "irdaobex://",
            0x1D => "file://",
            0x1E => "urn:epc:id:",
            0x1F => "urn:epc:tag:",
            0x20 => "urn:epc:pat:",
            0x21 => "urn:epc:raw:",
            0x22 => "urn:epc:",
            0x23 => "urn:nfc:",
            _ => ""
        };
    }
}
