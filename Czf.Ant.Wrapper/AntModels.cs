using ANT_Managed_Library;

namespace Czf.Ant.Wrapper;

/// <summary>
/// Well-known ANT+ device type identifiers.
/// </summary>
public enum AntKnownDeviceType : byte
{
    /// <summary>Heart Rate Monitor — device type ID 120 (0x78).</summary>
    HeartRateMonitor = 120,
}

/// <summary>
/// Represents the connection state of a remote ANT sensor on the channel.
/// This is distinct from the USB device / session state.
/// </summary>
public enum AntSensorConnectionState
{
    /// <summary>The sensor state is not yet determined.</summary>
    Unknown,

    /// <summary>The channel is open and scanning/searching for a matching sensor.</summary>
    Searching,

    /// <summary>A matching sensor has been found and is actively transmitting data.</summary>
    Tracking,

    /// <summary>The sensor link has been lost or the channel has been closed.</summary>
    Disconnected,
}

/// <summary>
/// Describes an ANT USB device detected on the host machine.
/// When <see cref="ProbeError"/> is non-null the device was detected but could not be opened;
/// USB detail properties will be <c>null</c>.
/// </summary>
/// <param name="UsbDeviceNumber">Zero-based index of the USB device as seen by the ANT library.</param>
/// <param name="ConnectedBaudRate">Baud rate at which the device was successfully opened, or <c>null</c> if not opened.</param>
/// <param name="ProductDescription">USB product description string, or <c>null</c> if not available.</param>
/// <param name="SerialString">USB serial number string, or <c>null</c> if not available.</param>
/// <param name="SerialNumber">ANT device serial number, or <c>null</c> if not available.</param>
/// <param name="UsbVendorId">USB vendor ID, or <c>null</c> if not available.</param>
/// <param name="UsbProductId">USB product ID, or <c>null</c> if not available.</param>
/// <param name="ProbeError">Non-null error message if the device was detected but could not be probed.</param>
public sealed record AntAvailableDevice(
    byte UsbDeviceNumber,
    uint? ConnectedBaudRate = null,
    string? ProductDescription = null,
    string? SerialString = null,
    uint? SerialNumber = null,
    ushort? UsbVendorId = null,
    ushort? UsbProductId = null,
    string? ProbeError = null);

/// <summary>
/// Identifies a specific remote ANT sensor device as reported in extended message data.
/// </summary>
/// <param name="DeviceNumber">ANT device number assigned to the sensor (0 = wildcard / any).</param>
/// <param name="DeviceTypeId">ANT device type ID (e.g. 120 for a heart rate monitor).</param>
/// <param name="TransmissionTypeId">ANT transmission type.</param>
/// <param name="PairingBit">Whether the pairing bit is set in the extended ID.</param>
public sealed record AntDeviceIdentity(
    ushort DeviceNumber,
    byte DeviceTypeId,
    byte TransmissionTypeId,
    bool PairingBit);

/// <summary>
/// Immutable snapshot of the capability flags reported by an ANT USB device.
/// </summary>
/// <param name="MaxAntChannels">Maximum number of ANT channels the device supports.</param>
/// <param name="MaxNetworks">Maximum number of networks the device supports.</param>
/// <param name="MaxDataChannels">Maximum number of data channels.</param>
/// <param name="ExtendedMessaging">Whether the device supports extended messaging (device ID in message).</param>
/// <param name="ScanModeSupport">Whether the device supports RX scan mode (promiscuous scanning across all channels).</param>
/// <param name="SerialNumber">Whether the device has a persistent serial number.</param>
/// <param name="SearchList">Whether the device supports a search list for pairing.</param>
/// <param name="SearchSharing">Whether the device supports search sharing.</param>
/// <param name="HighDutySearch">Whether the device supports high-duty search.</param>
public sealed record AntDeviceCapabilitiesSnapshot(
    byte MaxAntChannels,
    byte MaxNetworks,
    byte MaxDataChannels,
    bool ExtendedMessaging,
    bool ScanModeSupport,
    bool SerialNumber,
    bool SearchList,
    bool SearchSharing,
    bool HighDutySearch);

/// <summary>
/// Immutable snapshot of USB and capability information for an open ANT device,
/// captured at connection time.
/// </summary>
/// <param name="UsbDeviceNumber">Zero-based USB device index.</param>
/// <param name="BaudRate">Baud rate at which the device was opened.</param>
/// <param name="SerialNumber">ANT device serial number.</param>
/// <param name="UsbVendorId">USB vendor ID.</param>
/// <param name="UsbProductId">USB product ID.</param>
/// <param name="ProductDescription">USB product description string.</param>
/// <param name="SerialString">USB serial number string.</param>
/// <param name="Capabilities">Hardware capability flags.</param>
public sealed record AntDeviceSnapshot(
    byte UsbDeviceNumber,
    uint BaudRate,
    uint SerialNumber,
    ushort UsbVendorId,
    ushort UsbProductId,
    string ProductDescription,
    string SerialString,
    AntDeviceCapabilitiesSnapshot Capabilities);

/// <summary>
/// Immutable snapshot of the current state of an <see cref="AntDeviceConnection"/>.
/// </summary>
/// <param name="IsDeviceOpen">Whether the underlying ANT USB device is open and operable.</param>
/// <param name="IsListening">Whether a channel is currently open and listening/scanning.</param>
/// <param name="SensorConnectionState">The remote sensor link state on the active channel.</param>
/// <param name="HasReceivedData">Whether at least one data message has been successfully received since listening began.</param>
public sealed record AntConnectionState(
    bool IsDeviceOpen,
    bool IsListening,
    AntSensorConnectionState SensorConnectionState,
    bool HasReceivedData);

/// <summary>
/// Configuration options for opening an ANT channel in slave receive mode.
/// All properties have defaults suitable for general-purpose scanning.
/// Call <see cref="Validate"/> before passing to <see cref="AntDeviceConnection.StartListening"/>.
/// </summary>
public sealed class AntListeningOptions
{
    /// <summary>ANT channel number to use (default: 0).</summary>
    public byte ChannelNumber { get; init; } = 0;

    /// <summary>ANT network number to assign the channel to (default: 0).</summary>
    public byte NetworkNumber { get; init; } = 0;

    /// <summary>
    /// Optional 8-byte network key. When <c>null</c> the device's default key is used.
    /// Must be exactly 8 bytes if provided; see <see cref="Validate"/>.
    /// </summary>
    public byte[]? NetworkKey { get; init; }

    /// <summary>
    /// Target device number to listen for (default: 0 = any device).
    /// </summary>
    public ushort DeviceNumber { get; init; } = 0;

    /// <summary>
    /// ANT device type ID to filter on (e.g. 120 for heart rate monitor; 0 = any type).
    /// </summary>
    public byte DeviceTypeId { get; init; }

    /// <summary>
    /// Overrides the device type used for profile parsing without changing the channel filter.
    /// When <c>null</c>, <see cref="DeviceTypeId"/> is used for both filtering and parsing.
    /// </summary>
    public byte? KnownDeviceTypeId { get; init; }

    /// <summary>ANT transmission type to filter on (default: 0 = any).</summary>
    public byte TransmissionTypeId { get; init; } = 0;

    /// <summary>Whether to enable the pairing bit on the channel ID (default: false).</summary>
    public bool PairingEnabled { get; init; }

    /// <summary>
    /// RF frequency offset from 2400 MHz (e.g. 57 = 2457 MHz for standard ANT+ profiles).
    /// Default: 0 — callers must set this to the correct value for their target device profile.
    /// </summary>
    public byte RadioFrequency { get; init; }

    /// <summary>
    /// ANT channel period in 1/32768 second units, or <c>null</c> to use the device default.
    /// Typical ANT+ profiles define a fixed period (e.g. 8070 for heart rate monitors).
    /// </summary>
    public ushort? ChannelPeriod { get; init; }

    /// <summary>
    /// High-priority search timeout in 2.5-second increments (default: 255 = infinite).
    /// Set to 0 to skip high-priority search entirely.
    /// </summary>
    public byte SearchTimeout { get; init; } = 255;

    /// <summary>
    /// Low-priority search timeout in 2.5-second increments, or <c>null</c> to skip configuring it.
    /// </summary>
    public byte? LowPrioritySearchTimeout { get; init; }

    /// <summary>
    /// When <c>true</c>, opens the device in RX scan mode (promiscuous, all channels).
    /// Requires <see cref="AntDeviceCapabilitiesSnapshot.ScanModeSupport"/>; default: <c>true</c>.
    /// </summary>
    public bool UseRxScanMode { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, requests extended messages so device identity is included in each message.
    /// Only takes effect if the device supports <see cref="AntDeviceCapabilitiesSnapshot.ExtendedMessaging"/>.
    /// Default: <c>true</c>.
    /// </summary>
    public bool EnableExtendedMessages { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, requests that timestamps are included in extended messages (default: <c>true</c>).
    /// Requires <see cref="EnableExtendedMessages"/> to also be <c>true</c>.
    /// </summary>
    public bool IncludeTimestampInMessages { get; init; } = true;

    /// <summary>Milliseconds to wait for each synchronous ANT command response (default: 500).</summary>
    public uint ResponseWaitTimeMs { get; init; } = 500;

    /// <summary>
    /// Returns <see cref="KnownDeviceTypeId"/> if set, otherwise <see cref="DeviceTypeId"/>.
    /// Used by profile parsers to identify the device type when parsing incoming messages.
    /// </summary>
    public byte ResolvedKnownDeviceTypeId => KnownDeviceTypeId ?? DeviceTypeId;

    /// <summary>
    /// Validates the options and throws if any setting is invalid.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="NetworkKey"/> is non-null and not exactly 8 bytes.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="ResponseWaitTimeMs"/> is zero.
    /// </exception>
    public void Validate()
    {
        if (NetworkKey is not null && NetworkKey.Length != 8)
        {
            throw new ArgumentException("Network key must be exactly 8 bytes when provided.", nameof(NetworkKey));
        }

        if (ResponseWaitTimeMs == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ResponseWaitTimeMs), "Response wait time must be greater than zero.");
        }
    }
}

/// <summary>
/// ANT+ network constants shared across all ANT+ consumer device profiles.
/// </summary>
public static class AntPlusNetworks
{
    /// <summary>
    /// The public ANT+ network key used by all ANT+ certified consumer devices.
    /// Required when communicating with heart rate monitors, bike sensors, and other ANT+ profiles.
    /// </summary>
    public static readonly byte[] PublicNetworkKey = [0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45];

    /// <summary>The ANT+ standard RF frequency offset from 2400 MHz (57 = 2457 MHz).</summary>
    public const byte StandardRadioFrequency = 57;
}

/// <summary>
/// Convenience options for discovering nearby ANT+ devices using RX scan mode.
/// Pre-populates the public ANT+ network key and RF frequency so all ANT+ consumer
/// devices are visible without additional configuration.
/// Use the <see cref="AntDeviceConnection.DeviceDiscovered"/> event to receive a notification
/// the first time each unique device is seen.
/// </summary>
public sealed class AntScanOptions
{
    /// <summary>ANT channel number to use (default: 0).</summary>
    public byte ChannelNumber { get; init; } = 0;

    /// <summary>ANT network number (default: 0).</summary>
    public byte NetworkNumber { get; init; } = 0;

    /// <summary>
    /// 8-byte ANT+ network key (default: <see cref="AntPlusNetworks.PublicNetworkKey"/>).
    /// Set to <c>null</c> to skip configuring the network key.
    /// </summary>
    public byte[]? NetworkKey { get; init; } = AntPlusNetworks.PublicNetworkKey;

    /// <summary>
    /// ANT device type ID to filter on (default: 0 = accept any device type).
    /// Set to a specific value (e.g. 120 for HRM) to narrow the scan.
    /// </summary>
    public byte DeviceTypeId { get; init; } = 0;

    /// <summary>RF frequency offset from 2400 MHz (default: 57 = 2457 MHz, the ANT+ standard frequency).</summary>
    public byte RadioFrequency { get; init; } = AntPlusNetworks.StandardRadioFrequency;

    /// <summary>High-priority search timeout in 2.5-second increments (default: 255 = infinite).</summary>
    public byte SearchTimeout { get; init; } = 255;

    /// <summary>When <c>true</c>, requests extended messages to receive device identity (default: <c>true</c>).</summary>
    public bool EnableExtendedMessages { get; init; } = true;

    /// <summary>When <c>true</c>, requests timestamps in extended messages (default: <c>true</c>).</summary>
    public bool IncludeTimestampInMessages { get; init; } = true;

    /// <summary>Milliseconds to wait for each synchronous ANT command response (default: 500).</summary>
    public uint ResponseWaitTimeMs { get; init; } = 500;

    /// <summary>
    /// Converts these scan options into a fully configured <see cref="AntListeningOptions"/>
    /// with <see cref="AntListeningOptions.UseRxScanMode"/> set to <c>true</c>.
    /// </summary>
    public AntListeningOptions ToListeningOptions() => new()
    {
        ChannelNumber = ChannelNumber,
        NetworkNumber = NetworkNumber,
        NetworkKey = NetworkKey,
        DeviceTypeId = DeviceTypeId,
        RadioFrequency = RadioFrequency,
        SearchTimeout = SearchTimeout,
        UseRxScanMode = true,
        EnableExtendedMessages = EnableExtendedMessages,
        IncludeTimestampInMessages = IncludeTimestampInMessages,
        ResponseWaitTimeMs = ResponseWaitTimeMs,
    };
}

/// <summary>
/// Convenience options for connecting to an ANT+ Heart Rate Monitor.
/// Pre-populates protocol defaults (device type, RF frequency, channel period) and
/// converts to a full <see cref="AntListeningOptions"/> via <see cref="ToListeningOptions"/>.
/// </summary>
public sealed class AntHeartRateMonitorOptions
{
    /// <summary>
    /// The public ANT+ network key. Equivalent to <see cref="AntPlusNetworks.PublicNetworkKey"/>.
    /// </summary>
    public static readonly byte[] PublicNetworkKey = AntPlusNetworks.PublicNetworkKey;

    /// <summary>ANT channel number to use (default: 0).</summary>
    public byte ChannelNumber { get; init; } = 0;

    /// <summary>ANT network number (default: 0).</summary>
    public byte NetworkNumber { get; init; } = 0;

    /// <summary>
    /// 8-byte ANT+ network key (default: <see cref="PublicNetworkKey"/>).
    /// Set to <c>null</c> to skip configuring the network key entirely (uses whatever key is already loaded).
    /// </summary>
    public byte[]? NetworkKey { get; init; } = PublicNetworkKey;

    /// <summary>Target device number (default: 0 = any HRM).</summary>
    public ushort DeviceNumber { get; init; } = 0;

    /// <summary>ANT transmission type filter (default: 0 = any).</summary>
    public byte TransmissionTypeId { get; init; } = 0;

    /// <summary>Whether to enable the pairing bit (default: false).</summary>
    public bool PairingEnabled { get; init; }

    /// <summary>RF frequency offset from 2400 MHz (default: 57 = 2457 MHz, the ANT+ standard frequency).</summary>
    public byte RadioFrequency { get; init; } = 57;

    /// <summary>ANT channel period in 1/32768 s units (default: 8070 = ~4.06 Hz, the HRM standard period).</summary>
    public ushort ChannelPeriod { get; init; } = 8070;

    /// <summary>High-priority search timeout in 2.5-second increments (default: 255 = infinite).</summary>
    public byte SearchTimeout { get; init; } = 255;

    /// <summary>Low-priority search timeout in 2.5-second increments, or <c>null</c> to skip.</summary>
    public byte? LowPrioritySearchTimeout { get; init; }

    /// <summary>When <c>true</c>, opens the device in RX scan mode (default: <c>true</c>).</summary>
    public bool UseRxScanMode { get; init; } = true;

    /// <summary>When <c>true</c>, requests extended messages to receive device identity (default: <c>true</c>).</summary>
    public bool EnableExtendedMessages { get; init; } = true;

    /// <summary>When <c>true</c>, requests timestamps in extended messages (default: <c>true</c>).</summary>
    public bool IncludeTimestampInMessages { get; init; } = true;

    /// <summary>Milliseconds to wait for each synchronous ANT command response (default: 500).</summary>
    public uint ResponseWaitTimeMs { get; init; } = 500;

    /// <summary>
    /// Converts these options into a fully configured <see cref="AntListeningOptions"/>
    /// with the device type and known device type set to <see cref="AntKnownDeviceType.HeartRateMonitor"/>.
    /// </summary>
    public AntListeningOptions ToListeningOptions()
    {
        return new AntListeningOptions
        {
            ChannelNumber = ChannelNumber,
            NetworkNumber = NetworkNumber,
            NetworkKey = NetworkKey,
            DeviceNumber = DeviceNumber,
            DeviceTypeId = (byte)AntKnownDeviceType.HeartRateMonitor,
            KnownDeviceTypeId = (byte)AntKnownDeviceType.HeartRateMonitor,
            TransmissionTypeId = TransmissionTypeId,
            PairingEnabled = PairingEnabled,
            RadioFrequency = RadioFrequency,
            ChannelPeriod = ChannelPeriod,
            SearchTimeout = SearchTimeout,
            LowPrioritySearchTimeout = LowPrioritySearchTimeout,
            UseRxScanMode = UseRxScanMode,
            EnableExtendedMessages = EnableExtendedMessages,
            IncludeTimestampInMessages = IncludeTimestampInMessages,
            ResponseWaitTimeMs = ResponseWaitTimeMs,
        };
    }
}

/// <summary>
/// A decoded ANT message received on a channel.
/// </summary>
/// <param name="ChannelNumber">Zero-based ANT channel number the message arrived on.</param>
/// <param name="Timestamp">Timestamp at which the message was processed by the wrapper.</param>
/// <param name="ResponseId">The raw ANT response ID byte that classifies the message type.</param>
/// <param name="RawMessageBytes">Raw message bytes as received from the ANT library, before any parsing.</param>
/// <param name="DataPayload">
/// Extracted 8-byte data payload for data messages, or <c>null</c> if extraction failed.
/// Check <see cref="IsDataMessage"/> for a convenient null test.
/// </param>
/// <param name="DeviceIdentity">
/// Device identity extracted from extended message data, or <c>null</c> if not available.
/// Populated when <see cref="IsDataMessage"/> is <c>true</c> and extended messaging is enabled.
/// </param>
/// <param name="ChannelEventCode">
/// ANT channel event code, or <c>null</c> if this is not a <c>RESPONSE_EVENT</c> message.
/// See <c>ANT_ReferenceLibrary.ANTEventID</c> for values.
/// </param>
/// <param name="ChannelMessageId">
/// The message ID field from a <c>RESPONSE_EVENT</c> message, or <c>null</c> if not applicable.
/// </param>
public sealed record AntRawMessage(
    byte ChannelNumber,
    DateTime Timestamp,
    byte ResponseId,
    byte[] RawMessageBytes,
    byte[]? DataPayload,
    AntDeviceIdentity? DeviceIdentity,
    byte? ChannelEventCode,
    byte? ChannelMessageId)
{
    /// <summary>
    /// <c>true</c> when the response ID identifies this as a data message type,
    /// regardless of whether the payload was successfully extracted.
    /// </summary>
    public bool IsDataMessageType { get; init; }

    /// <summary>
    /// <c>true</c> when the data payload was successfully extracted.
    /// When <see cref="IsDataMessageType"/> is <c>true</c> but this is <c>false</c>,
    /// the message arrived but payload extraction failed.
    /// </summary>
    public bool IsDataMessage => DataPayload is not null;
}

/// <summary>
/// Decoded data from a single ANT+ Heart Rate Monitor broadcast.
/// </summary>
/// <param name="PageNumber">ANT+ HRM data page number (lower 7 bits of byte 0).</param>
/// <param name="ToggleBitSet">Whether the toggle bit (bit 7 of byte 0) was set in this transmission.</param>
/// <param name="ComputedHeartRate">Computed heart rate in beats per minute (byte 7 of the payload).</param>
/// <param name="BeatCount">Cumulative beat count (byte 6 of the payload).</param>
/// <param name="BeatTime">Time of the last valid beat event in 1/1024 second units (little-endian bytes 4–5).</param>
/// <param name="DeviceIdentity">Extended device identity from the message, or <c>null</c> if not available.</param>
/// <param name="Timestamp">Timestamp at which the message was received.</param>
public sealed record HeartRateData(
    byte PageNumber,
    bool ToggleBitSet,
    byte ComputedHeartRate,
    byte BeatCount,
    ushort BeatTime,
    AntDeviceIdentity? DeviceIdentity,
    DateTime Timestamp);

/// <summary>
/// Event arguments for <see cref="AntDeviceConnection.StateChanged"/>.
/// </summary>
public sealed class AntConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="previousState">The state before the transition.</param>
    /// <param name="currentState">The state after the transition.</param>
    public AntConnectionStateChangedEventArgs(AntConnectionState previousState, AntConnectionState currentState)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }

    /// <summary>The connection state immediately before the transition.</summary>
    public AntConnectionState PreviousState { get; }

    /// <summary>The connection state immediately after the transition.</summary>
    public AntConnectionState CurrentState { get; }
}

/// <summary>
/// Event arguments for <see cref="AntDeviceConnection.RawMessageReceived"/>.
/// </summary>
public sealed class AntRawMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The decoded message that was received.</param>
    public AntRawMessageReceivedEventArgs(AntRawMessage message)
    {
        Message = message;
    }

    /// <summary>The decoded ANT message that was received.</summary>
    public AntRawMessage Message { get; }
}

/// <summary>
/// Event arguments for <see cref="AntDeviceConnection.HeartRateReceived"/>.
/// </summary>
public sealed class HeartRateDataReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="data">The parsed heart rate data.</param>
    /// <param name="rawMessage">The underlying raw ANT message.</param>
    public HeartRateDataReceivedEventArgs(HeartRateData data, AntRawMessage rawMessage)
    {
        Data = data;
        RawMessage = rawMessage;
    }

    /// <summary>The parsed heart rate data.</summary>
    public HeartRateData Data { get; }

    /// <summary>The underlying raw ANT message from which the data was parsed.</summary>
    public AntRawMessage RawMessage { get; }
}

/// <summary>
/// Wrapper-owned mirror of <c>ANT_Device.serialErrorCode</c>.
/// Lets consumers handle device errors without a direct reference to <c>ANT_Managed_Library</c>.
/// </summary>
public enum AntSerialErrorCode
{
    /// <summary>A write command to the device failed (USB communication issue or invalid parameters).</summary>
    SerialWriteError,
    /// <summary>A failure occurred reading data from the device.</summary>
    SerialReadError,
    /// <summary>Communication with the device has been lost (unrecoverable).</summary>
    DeviceConnectionLost,
    /// <summary>A received message failed the CRC check and was discarded.</summary>
    MessageLost_CrcError,
    /// <summary>The receive message queue overflowed; one or more messages were lost.</summary>
    MessageLost_QueueOverflow,
    /// <summary>A received message exceeded the maximum size and was discarded.</summary>
    MessageLost_TooLarge,
    /// <summary>A channel event arrived for a channel that does not exist.</summary>
    MessageLost_InvalidChannel,
    /// <summary>Unspecified or unrecognised error code.</summary>
    Unknown,
}

/// <summary>
/// Event arguments for <see cref="AntDeviceConnection.DeviceError"/>.
/// </summary>
public sealed class AntDeviceErrorEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="errorCode">The serial error code reported by the ANT library.</param>
    /// <param name="isCritical">
    /// <c>true</c> if the error is unrecoverable and the device object is no longer usable.
    /// </param>
    public AntDeviceErrorEventArgs(AntSerialErrorCode errorCode, bool isCritical)
    {
        ErrorCode = errorCode;
        IsCritical = isCritical;
    }

    /// <summary>The serial error code reported by the ANT library.</summary>
    public AntSerialErrorCode ErrorCode { get; }

    /// <summary>
    /// <c>true</c> when the error is unrecoverable (<c>DeviceConnectionLost</c> and similar).
    /// After a critical error the <see cref="AntDeviceConnection"/> must be disposed.
    /// </summary>
    public bool IsCritical { get; }
}

/// <summary>
/// Event arguments for <see cref="AntDeviceConnection.DeviceDiscovered"/>.
/// Raised the first time a unique ANT device broadcasts during a scan session.
/// </summary>
public sealed class AntDeviceDiscoveredEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="identity">The device identity extracted from the extended message.</param>
    /// <param name="firstMessage">The first raw message received from this device.</param>
    public AntDeviceDiscoveredEventArgs(AntDeviceIdentity identity, AntRawMessage firstMessage)
    {
        Identity = identity;
        FirstMessage = firstMessage;
    }

    /// <summary>The device's ANT identity (number, type, transmission type, pairing bit).</summary>
    public AntDeviceIdentity Identity { get; }

    /// <summary>The first raw ANT message received from this device during the current scan.</summary>
    public AntRawMessage FirstMessage { get; }
}

