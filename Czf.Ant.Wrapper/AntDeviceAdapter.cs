using ANT_Managed_Library;

namespace Czf.Ant.Wrapper;

internal sealed class AntDeviceAdapter : IAntDevice
{
    private readonly ANT_Device _device;

    internal AntDeviceAdapter(ANT_Device device)
    {
        _device = device;
        _device.serialError += OnRealSerialError;
    }

    public event Action<ANT_Device.serialErrorCode, bool>? SerialError;

    private void OnRealSerialError(ANT_Device sender, ANT_Device.serialErrorCode error, bool isCritical)
        => SerialError?.Invoke(error, isCritical);

    public void SetNetworkKey(byte networkNumber, byte[] networkKey, uint responseWaitTime)
        => _device.setNetworkKey(networkNumber, networkKey, responseWaitTime);

    public void EnableRxExtendedMessages(bool enable, uint responseWaitTime)
        => _device.enableRxExtendedMessages(enable, responseWaitTime);

    public void SetLibConfig(ANT_ReferenceLibrary.LibConfigFlags flags, uint responseWaitTime)
        => _device.setLibConfig(flags, responseWaitTime);

    public IAntChannel GetChannel(byte channelNumber)
        => new AntChannelAdapter(_device.getChannel(channelNumber));

    public void OpenRxScanMode(uint responseWaitTime)
        => _device.openRxScanMode(responseWaitTime);

    public void Dispose()
    {
        _device.serialError -= OnRealSerialError;
        _device.Dispose();
    }
}
