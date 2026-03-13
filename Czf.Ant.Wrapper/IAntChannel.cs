using ANT_Managed_Library;

namespace Czf.Ant.Wrapper;

/// <summary>
/// Abstraction over a single ANT channel, allowing the connection layer to be
/// tested independently of hardware. <see cref="ChannelResponse"/> fires a fully
/// decoded <see cref="AntRawMessage"/> so callers never need to construct
/// <c>ANT_Response</c> directly.
/// </summary>
public interface IAntChannel
{
    /// <summary>Fired on the ANT background thread for device-level notifications.</summary>
    event Action<ANT_Device.DeviceNotificationCode, object>? DeviceNotification;

    /// <summary>
    /// Fired on the ANT background thread for every incoming message on this channel.
    /// The message has already been decoded into an <see cref="AntRawMessage"/>.
    /// </summary>
    event Action<AntRawMessage>? ChannelResponse;

    /// <summary>Assigns the channel type and network to this channel before opening.</summary>
    /// <param name="channelType">Slave or master, with or without shared/scan modifiers.</param>
    /// <param name="networkNumber">Network index (typically 0).</param>
    /// <param name="responseWaitTime">Milliseconds to wait for a response.</param>
    void AssignChannel(ANT_ReferenceLibrary.ChannelType channelType, byte networkNumber, uint responseWaitTime);

    /// <summary>Sets the channel device number, type, and transmission type for slave pairing.</summary>
    /// <param name="deviceNumber">Target device number; 0 matches any device.</param>
    /// <param name="pairingEnabled">When <c>true</c>, the pairing bit is set in the channel ID.</param>
    /// <param name="deviceTypeId">ANT+ device type byte (e.g., 120 for heart rate).</param>
    /// <param name="transmissionTypeId">Transmission type byte; 0 matches any.</param>
    /// <param name="responseWaitTime">Milliseconds to wait for a response.</param>
    void SetChannelId(ushort deviceNumber, bool pairingEnabled, byte deviceTypeId, byte transmissionTypeId, uint responseWaitTime);

    /// <summary>Sets the RF frequency offset for this channel.</summary>
    /// <param name="radioFrequency">Offset from 2400 MHz (e.g., 57 = 2457 MHz for ANT+).</param>
    /// <param name="responseWaitTime">Milliseconds to wait for a response.</param>
    void SetChannelFreq(byte radioFrequency, uint responseWaitTime);

    /// <summary>Sets the channel message period.</summary>
    /// <param name="channelPeriod">Period in 1/32768 second increments (e.g., 8070 ≈ 4 Hz for HRM).</param>
    /// <param name="responseWaitTime">Milliseconds to wait for a response.</param>
    void SetChannelPeriod(ushort channelPeriod, uint responseWaitTime);

    /// <summary>Sets the high-priority search timeout (in 2.5-second increments; 255 = infinite).</summary>
    void SetChannelSearchTimeout(byte searchTimeout, uint responseWaitTime);

    /// <summary>Sets the low-priority search timeout (in 2.5-second increments; 0 = disabled).</summary>
    void SetLowPrioritySearchTimeout(byte searchTimeout, uint responseWaitTime);

    /// <summary>Opens the channel so it begins searching for / tracking a remote sensor.</summary>
    void OpenChannel(uint responseWaitTime);

    /// <summary>Closes the channel, stopping message reception.</summary>
    void CloseChannel(uint responseWaitTime);

    /// <summary>Unassigns the channel, freeing it for re-use.</summary>
    void UnassignChannel(uint responseWaitTime);

    /// <summary>Requests the current channel status from the device.</summary>
    /// <param name="responseWaitTime">Milliseconds to wait for a response.</param>
    ANT_ReferenceLibrary.BasicChannelStatusCode RequestStatus(uint responseWaitTime);
}
