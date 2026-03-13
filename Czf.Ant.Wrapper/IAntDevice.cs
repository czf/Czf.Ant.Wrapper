using ANT_Managed_Library;

namespace Czf.Ant.Wrapper;

/// <summary>
/// Abstraction over a physical ANT USB dongle, allowing the connection layer to be
/// tested independently of hardware.
/// </summary>
public interface IAntDevice : IDisposable
{
    /// <summary>Fired on the ANT background thread when a serial error occurs.</summary>
    event Action<ANT_Device.serialErrorCode, bool>? SerialError;

    /// <summary>Sets the network key for the specified network number.</summary>
    /// <param name="networkNumber">Network index (typically 0 for the public ANT+ network).</param>
    /// <param name="networkKey">8-byte network key.</param>
    /// <param name="responseWaitTime">Milliseconds to wait for a response from the device.</param>
    void SetNetworkKey(byte networkNumber, byte[] networkKey, uint responseWaitTime);

    /// <summary>Enables or disables extended ANT message delivery on this device.</summary>
    /// <param name="enable"><c>true</c> to enable; <c>false</c> to disable.</param>
    /// <param name="responseWaitTime">Milliseconds to wait for a response from the device.</param>
    void EnableRxExtendedMessages(bool enable, uint responseWaitTime);

    /// <summary>Sets the library configuration flags that control extended message content.</summary>
    /// <param name="flags">Bitmask of <see cref="ANT_ReferenceLibrary.LibConfigFlags"/> values.</param>
    /// <param name="responseWaitTime">Milliseconds to wait for a response from the device.</param>
    void SetLibConfig(ANT_ReferenceLibrary.LibConfigFlags flags, uint responseWaitTime);

    /// <summary>Returns (or lazily opens) the channel at the given index.</summary>
    /// <param name="channelNumber">Zero-based channel index.</param>
    IAntChannel GetChannel(byte channelNumber);

    /// <summary>
    /// Switches the device into RX scan mode, which listens on all channels simultaneously.
    /// This operation cannot be undone without resetting the device.
    /// </summary>
    /// <param name="responseWaitTime">Milliseconds to wait for a response from the device.</param>
    void OpenRxScanMode(uint responseWaitTime);
}
