using ANT_Managed_Library;

namespace Czf.Ant.Wrapper;

internal sealed class AntChannelAdapter : IAntChannel
{
    private readonly ANT_Channel _channel;

    internal AntChannelAdapter(ANT_Channel channel)
    {
        _channel = channel;
        _channel.channelResponse += OnRealChannelResponse;
        _channel.DeviceNotification += OnRealDeviceNotification;
    }

    public event Action<ANT_Device.DeviceNotificationCode, object>? DeviceNotification;
    public event Action<AntRawMessage>? ChannelResponse;

    private void OnRealChannelResponse(ANT_Response response)
        => ChannelResponse?.Invoke(CreateRawMessage(response));

    private void OnRealDeviceNotification(ANT_Device.DeviceNotificationCode notification, object notificationInfo)
        => DeviceNotification?.Invoke(notification, notificationInfo);

    public void AssignChannel(ANT_ReferenceLibrary.ChannelType channelType, byte networkNumber, uint responseWaitTime)
        => _channel.assignChannel(channelType, networkNumber, responseWaitTime);

    public void SetChannelId(ushort deviceNumber, bool pairingEnabled, byte deviceTypeId, byte transmissionTypeId, uint responseWaitTime)
        => _channel.setChannelID(deviceNumber, pairingEnabled, deviceTypeId, transmissionTypeId, responseWaitTime);

    public void SetChannelFreq(byte radioFrequency, uint responseWaitTime)
        => _channel.setChannelFreq(radioFrequency, responseWaitTime);

    public void SetChannelPeriod(ushort channelPeriod, uint responseWaitTime)
        => _channel.setChannelPeriod(channelPeriod, responseWaitTime);

    public void SetChannelSearchTimeout(byte searchTimeout, uint responseWaitTime)
        => _channel.setChannelSearchTimeout(searchTimeout, responseWaitTime);

    public void SetLowPrioritySearchTimeout(byte searchTimeout, uint responseWaitTime)
        => _channel.setLowPrioritySearchTimeout(searchTimeout, responseWaitTime);

    public void OpenChannel(uint responseWaitTime)
        => _channel.openChannel(responseWaitTime);

    public void CloseChannel(uint responseWaitTime)
        => _channel.closeChannel(responseWaitTime);

    public void UnassignChannel(uint responseWaitTime)
        => _channel.unassignChannel(responseWaitTime);

    public ANT_ReferenceLibrary.BasicChannelStatusCode RequestStatus(uint responseWaitTime)
        => _channel.requestStatus(responseWaitTime).BasicStatus;

    private static AntRawMessage CreateRawMessage(ANT_Response response)
    {
        var isDataMessageType = response.responseID is
            (byte)ANT_ReferenceLibrary.ANTMessageID.BROADCAST_DATA_0x4E or
            (byte)ANT_ReferenceLibrary.ANTMessageID.ACKNOWLEDGED_DATA_0x4F or
            (byte)ANT_ReferenceLibrary.ANTMessageID.BURST_DATA_0x50 or
            (byte)ANT_ReferenceLibrary.ANTMessageID.EXT_BROADCAST_DATA_0x5D or
            (byte)ANT_ReferenceLibrary.ANTMessageID.EXT_ACKNOWLEDGED_DATA_0x5E or
            (byte)ANT_ReferenceLibrary.ANTMessageID.EXT_BURST_DATA_0x5F;

        byte[]? dataPayload = null;
        AntDeviceIdentity? deviceIdentity = null;
        byte? channelEventCode = null;
        byte? channelMessageId = null;

        if (isDataMessageType)
        {
            try
            {
                dataPayload = response.getDataPayload();
            }
            catch (ANT_Exception)
            {
                // Payload could not be extracted; treat message as opaque.
            }

            if (dataPayload is not null && response.isExtended())
            {
                try
                {
                    var channelId = response.getDeviceIDfromExt();
                    deviceIdentity = new AntDeviceIdentity(
                        channelId.deviceNumber,
                        channelId.deviceTypeID,
                        channelId.transmissionTypeID,
                        channelId.pairingBit);
                }
                catch (ANT_Exception)
                {
                    // Extended device ID could not be extracted; identity stays null.
                }
            }
        }
        else if (response.responseID == (byte)ANT_ReferenceLibrary.ANTMessageID.RESPONSE_EVENT_0x40)
        {
            try
            {
                channelEventCode = (byte)response.getChannelEventCode();
                channelMessageId = (byte)response.getMessageID();
            }
            catch (ANT_Exception)
            {
                // Could not extract event code or message ID; leave both null.
            }
        }

        return new AntRawMessage(
            response.antChannel,
            response.timeReceived,
            response.responseID,
            response.messageContents,
            dataPayload,
            deviceIdentity,
            channelEventCode,
            channelMessageId)
        {
            IsDataMessageType = isDataMessageType,
        };
    }
}
