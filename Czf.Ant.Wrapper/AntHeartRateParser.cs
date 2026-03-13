namespace Czf.Ant.Wrapper;

/// <summary>
/// Stateless parser for ANT+ Heart Rate Monitor (device type 120) broadcast messages.
/// Payload layout follows the ANT+ Heart Rate Device Profile specification:
/// byte 0 = page byte (lower 7 bits = page number, bit 7 = toggle bit),
/// bytes 1–3 = page-specific data, bytes 4–5 = beat event time (little-endian),
/// byte 6 = beat count, byte 7 = computed heart rate (BPM).
/// </summary>
public static class AntHeartRateParser
{
    /// <summary>
    /// Attempts to parse a heart rate broadcast from an <see cref="AntRawMessage"/>.
    /// Returns <c>false</c> when <see cref="AntRawMessage.DataPayload"/> is <c>null</c>.
    /// </summary>
    /// <param name="message">The raw message to parse.</param>
    /// <param name="data">When this method returns <c>true</c>, the parsed heart rate data; otherwise <c>null</c>.</param>
    public static bool TryParse(AntRawMessage message, out HeartRateData? data)
    {
        if (message.DataPayload is null)
        {
            data = null;
            return false;
        }

        return TryParse(message.DataPayload, message.DeviceIdentity, message.Timestamp, out data);
    }

    /// <summary>
    /// Attempts to parse a heart rate broadcast from a raw 8-byte payload.
    /// Returns <c>false</c> when the payload length is not exactly 8 bytes.
    /// </summary>
    /// <param name="payload">8-byte ANT broadcast payload.</param>
    /// <param name="deviceIdentity">Optional device identity to embed in the result.</param>
    /// <param name="timestamp">Timestamp to embed in the result (typically <see cref="AntRawMessage.Timestamp"/>).</param>
    /// <param name="data">When this method returns <c>true</c>, the parsed heart rate data; otherwise <c>null</c>.</param>
    public static bool TryParse(
        byte[] payload,
        AntDeviceIdentity? deviceIdentity,
        DateTime timestamp,
        out HeartRateData? data)
    {
        if (payload.Length != 8)
        {
            data = null;
            return false;
        }

        var pageByte = payload[0];
        var pageNumber = (byte)(pageByte & 0x7F);
        var toggleBitSet = (pageByte & 0x80) != 0;
        var beatTime = (ushort)(payload[4] | (payload[5] << 8));

        data = new HeartRateData(
            pageNumber,
            toggleBitSet,
            payload[7],
            payload[6],
            beatTime,
            deviceIdentity,
            timestamp);

        return true;
    }
}

