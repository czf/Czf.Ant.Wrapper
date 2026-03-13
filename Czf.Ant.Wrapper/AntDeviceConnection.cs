using ANT_Managed_Library;

namespace Czf.Ant.Wrapper;

/// <summary>
/// Manages a single ANT USB dongle session: opens a channel, raises events for state changes
/// and incoming messages, and handles device lifecycle from first connection through disposal.
/// </summary>
/// <remarks>
/// <para>
/// Obtain an instance via <see cref="AntDeviceManager.Connect"/> or
/// <see cref="AntDeviceManager.ConnectFirstAvailable"/>. Dispose the connection when done to
/// release the underlying USB device.
/// </para>
/// <para>
/// All events fire on the ANT background thread; marshal to the UI thread if required.
/// <see cref="State"/> is always consistent because transitions are performed under a lock.
/// </para>
/// </remarks>
public sealed class AntDeviceConnection : IDisposable, IAsyncDisposable
{
    private readonly object syncLock = new();
    private readonly IAntDevice device;
    private IAntChannel? channel;
    private volatile bool disposed;
    private volatile bool deviceFaulted;
    private AntListeningOptions? activeOptions;
    private bool rxScanModeActive;    // guarded by syncLock
    private bool isStartingListening; // guarded by syncLock
    private HashSet<(ushort DeviceNumber, byte DeviceTypeId)> seenDevices = []; // guarded by syncLock

    /// <summary>
    /// Initializes a new <see cref="AntDeviceConnection"/> wrapping an already-open ANT device.
    /// Prefer <see cref="AntDeviceManager.Connect"/> over calling this constructor directly.
    /// </summary>
    /// <param name="device">The abstracted ANT device. The connection takes ownership and disposes it.</param>
    /// <param name="snapshot">Immutable USB and capability information captured at open time.</param>
    public AntDeviceConnection(IAntDevice device, AntDeviceSnapshot snapshot)
    {
        this.device = device;
        Device = snapshot;
        UsbDeviceNumber = snapshot.UsbDeviceNumber;
        BaudRate = snapshot.BaudRate;
        State = new AntConnectionState(
            IsDeviceOpen: true,
            IsListening: false,
            SensorConnectionState: AntSensorConnectionState.Unknown,
            HasReceivedData: false);

        this.device.SerialError += OnSerialError;
    }

    /// <summary>
    /// Raised on the ANT background thread whenever <see cref="State"/> changes.
    /// The new state is available on the event args and on <see cref="State"/>.
    /// </summary>
    public event EventHandler<AntConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised on the ANT background thread for every incoming ANT message,
    /// including event messages (search timeout, channel closed, etc.).
    /// </summary>
    public event EventHandler<AntRawMessageReceivedEventArgs>? RawMessageReceived;

    /// <summary>
    /// Raised on the ANT background thread when an incoming message is successfully parsed
    /// as ANT+ Heart Rate Monitor data.
    /// Only fires when the listening options identify the device type as
    /// <see cref="AntKnownDeviceType.HeartRateMonitor"/>.
    /// </summary>
    public event EventHandler<HeartRateDataReceivedEventArgs>? HeartRateReceived;

    /// <summary>
    /// Raised on the ANT background thread the first time a unique ANT device is seen
    /// during the current listening session. Requires extended messages to be enabled
    /// (the default) so the device identity is included in broadcast messages.
    /// Resets each time <see cref="StartListening"/> (or a convenience overload) is called.
    /// </summary>
    public event EventHandler<AntDeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Raised on the ANT background thread when the ANT library reports a serial error.
    /// When <see cref="AntDeviceErrorEventArgs.IsCritical"/> is <c>true</c> the device is
    /// permanently unusable and this connection must be disposed.
    /// </summary>
    public event EventHandler<AntDeviceErrorEventArgs>? DeviceError;

    /// <summary>Zero-based USB device index that this connection is using.</summary>
    public byte UsbDeviceNumber { get; }

    /// <summary>Baud rate at which the USB device was opened.</summary>
    public uint BaudRate { get; }

    /// <summary>Immutable USB and capability snapshot captured when the device was opened.</summary>
    public AntDeviceSnapshot Device { get; }

    /// <summary>
    /// The current connection state. Updated atomically; consistent to read from any thread.
    /// Subscribe to <see cref="StateChanged"/> to be notified of transitions.
    /// </summary>
    public AntConnectionState State { get; private set; }

    /// <summary>
    /// Configures and opens an ANT channel using the supplied options, then begins receiving messages.
    /// </summary>
    /// <param name="options">
    /// Channel and device configuration. Must pass <see cref="AntListeningOptions.Validate"/>.
    /// </param>
    /// <exception cref="ObjectDisposedException">The connection has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// A channel is already listening, or scan mode was requested but the device does not support it,
    /// or the device has faulted due to a previous unrecoverable error.
    /// </exception>
    public void StartListening(AntListeningOptions options)
    {
        ThrowIfDisposed();
        options.Validate();

        if (options.UseRxScanMode && !Device.Capabilities.ScanModeSupport)
        {
            throw new InvalidOperationException(
                "This device does not support RX scan mode. Set UseRxScanMode = false.");
        }

        lock (syncLock)
        {
            if (State.IsListening || isStartingListening)
            {
                throw new InvalidOperationException("The connection is already listening on a channel.");
            }

            isStartingListening = true;
            activeOptions = options;
            seenDevices = [];
        }

        IAntChannel? configuredChannel = null;
        try
        {
            if (options.NetworkKey is { Length: 8 })
            {
                device.SetNetworkKey(options.NetworkNumber, options.NetworkKey, options.ResponseWaitTimeMs);
            }

            ConfigureExtendedMessages(options);

            configuredChannel = device.GetChannel(options.ChannelNumber);
            configuredChannel.DeviceNotification += OnDeviceNotification;
            configuredChannel.ChannelResponse += OnChannelResponse;

            configuredChannel.AssignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, options.NetworkNumber, options.ResponseWaitTimeMs);
            configuredChannel.SetChannelId(options.DeviceNumber, options.PairingEnabled, options.DeviceTypeId, options.TransmissionTypeId, options.ResponseWaitTimeMs);
            configuredChannel.SetChannelFreq(options.RadioFrequency, options.ResponseWaitTimeMs);

            if (options.ChannelPeriod.HasValue)
            {
                configuredChannel.SetChannelPeriod(options.ChannelPeriod.Value, options.ResponseWaitTimeMs);
            }

            configuredChannel.SetChannelSearchTimeout(options.SearchTimeout, options.ResponseWaitTimeMs);

            if (options.LowPrioritySearchTimeout.HasValue)
            {
                configuredChannel.SetLowPrioritySearchTimeout(options.LowPrioritySearchTimeout.Value, options.ResponseWaitTimeMs);
            }

            AntSensorConnectionState initialConnectionState;

            if (options.UseRxScanMode)
            {
                try
                {
                    device.OpenRxScanMode(options.ResponseWaitTimeMs);
                }
                catch
                {
                    // openRxScanMode cannot be rolled back if it partially executed.
                    deviceFaulted = true;
                    throw;
                }

                // BasicChannelStatusCode is only valid in normal channel mode.
                // In scan mode the device is scanning — treat as Searching unconditionally.
                initialConnectionState = AntSensorConnectionState.Searching;
            }
            else
            {
                configuredChannel.OpenChannel(options.ResponseWaitTimeMs);
                var basicStatus = configuredChannel.RequestStatus(options.ResponseWaitTimeMs);
                initialConnectionState = MapSensorConnectionState(basicStatus);
            }

            // Assign the channel field only after the hardware is successfully opened.
            lock (syncLock)
            {
                channel = configuredChannel;
                rxScanModeActive = options.UseRxScanMode;
                isStartingListening = false;
            }

            UpdateState(s => s with
            {
                IsListening = true,
                SensorConnectionState = initialConnectionState,
                HasReceivedData = false,
            });
        }
        catch
        {
            if (configuredChannel is not null)
            {
                configuredChannel.ChannelResponse -= OnChannelResponse;
                configuredChannel.DeviceNotification -= OnDeviceNotification;
            }

            lock (syncLock)
            {
                channel = null;
                activeOptions = null;
                rxScanModeActive = false;
                isStartingListening = false;
            }

            throw;
        }
    }

    /// <summary>
    /// Asynchronous wrapper around <see cref="StartListening"/>. The channel is opened synchronously;
    /// this method completes when the channel is open.
    /// </summary>
    /// <param name="options">Channel and device configuration.</param>
    /// <param name="cancellationToken">Checked before opening; cancellation after open is not supported.</param>
    public Task StartListeningAsync(AntListeningOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartListening(options);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens a channel pre-configured for the ANT+ Heart Rate Monitor profile
    /// and begins receiving messages. <see cref="HeartRateReceived"/> will fire on each broadcast.
    /// </summary>
    /// <param name="options">Heart rate monitor options; defaults are correct for standard ANT+ HRMs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public void StartHeartRateMonitoring(AntHeartRateMonitorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        StartListening(options.ToListeningOptions());
    }

    /// <summary>
    /// Asynchronous wrapper around <see cref="StartHeartRateMonitoring"/>.
    /// </summary>
    /// <param name="options">Heart rate monitor options.</param>
    /// <param name="cancellationToken">Checked before opening.</param>
    public Task StartHeartRateMonitoringAsync(AntHeartRateMonitorOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartHeartRateMonitoring(options);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens the device in RX scan mode to discover all nearby ANT+ devices.
    /// Subscribe to <see cref="DeviceDiscovered"/> to be notified when each new device
    /// is seen for the first time. Subscribe to <see cref="RawMessageReceived"/> for every
    /// message or <see cref="HeartRateReceived"/> for parsed HRM data if an HRM is also present.
    /// </summary>
    /// <param name="options">Scan options; defaults discover all ANT+ consumer devices.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public void StartScan(AntScanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        StartListening(options.ToListeningOptions());
    }

    /// <summary>
    /// Asynchronous wrapper around <see cref="StartScan"/>.
    /// </summary>
    /// <param name="options">Scan options.</param>
    /// <param name="cancellationToken">Checked before opening.</param>
    public Task StartScanAsync(AntScanOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartScan(options);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Closes the active ANT channel and stops receiving messages.
    /// In normal channel mode the channel is closed and unassigned on the device.
    /// In RX scan mode a full close is not possible; the state is updated but the device
    /// continues scanning until it is disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The connection has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The device has faulted.</exception>
    public void StopListening()
    {
        ThrowIfDisposed();

        IAntChannel? currentChannel;
        uint responseWaitTime;
        bool wasScanMode;

        lock (syncLock)
        {
            currentChannel = channel;
            responseWaitTime = activeOptions?.ResponseWaitTimeMs ?? 500;
            wasScanMode = rxScanModeActive;
            channel = null;
            activeOptions = null;
            rxScanModeActive = false;
        }

        if (currentChannel is null)
        {
            if (State.IsListening)
            {
                UpdateState(s => s with
                {
                    IsListening = false,
                    SensorConnectionState = AntSensorConnectionState.Unknown,
                    HasReceivedData = false,
                });
            }

            return;
        }

        // Scan mode cannot be stopped at the channel level — only a full device reset can undo
        // openRxScanMode(). Calling closeChannel()/unassignChannel() after scan mode is a no-op
        // at best and can corrupt device state at worst.
        if (!wasScanMode && State.IsListening)
        {
            currentChannel.CloseChannel(responseWaitTime);
            currentChannel.UnassignChannel(responseWaitTime);
        }

        currentChannel.ChannelResponse -= OnChannelResponse;
        currentChannel.DeviceNotification -= OnDeviceNotification;

        UpdateState(s => s with
        {
            IsListening = false,
            SensorConnectionState = AntSensorConnectionState.Unknown,
            HasReceivedData = false,
        });
    }

    /// <summary>
    /// Stops listening, disposes the underlying ANT device, and releases all resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronous wrapper around <see cref="Dispose()"/>. Disposal is synchronous; this completes immediately.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (disposing)
        {
            IAntChannel? currentChannel;
            lock (syncLock)
            {
                currentChannel = channel;
                channel = null;
                activeOptions = null;
                rxScanModeActive = false;
            }

            if (currentChannel is not null)
            {
                currentChannel.ChannelResponse -= OnChannelResponse;
                currentChannel.DeviceNotification -= OnDeviceNotification;
            }

            device.SerialError -= OnSerialError;

            UpdateState(s => new AntConnectionState(
                IsDeviceOpen: false,
                IsListening: false,
                SensorConnectionState: AntSensorConnectionState.Disconnected,
                HasReceivedData: s.HasReceivedData));

            device.Dispose();
        }
    }

    private void OnSerialError(ANT_Device.serialErrorCode error, bool isCritical)
    {
        if (disposed || deviceFaulted)
        {
            return;
        }

        DeviceError?.Invoke(this, new AntDeviceErrorEventArgs(ToPublicErrorCode(error), isCritical));

        if (isCritical)
        {
            IAntChannel? currentChannel;
            lock (syncLock)
            {
                currentChannel = channel;
                channel = null;
                activeOptions = null;
                rxScanModeActive = false;
            }

            if (currentChannel is not null)
            {
                currentChannel.ChannelResponse -= OnChannelResponse;
                currentChannel.DeviceNotification -= OnDeviceNotification;
            }

            device.SerialError -= OnSerialError;
            deviceFaulted = true;

            UpdateState(s => new AntConnectionState(
                IsDeviceOpen: false,
                IsListening: false,
                SensorConnectionState: AntSensorConnectionState.Disconnected,
                HasReceivedData: s.HasReceivedData));
        }
    }

    private void OnDeviceNotification(ANT_Device.DeviceNotificationCode notification, object notificationInfo)
    {
        if (disposed || deviceFaulted)
        {
            return;
        }

        if (notification is ANT_Device.DeviceNotificationCode.Reset or ANT_Device.DeviceNotificationCode.Shutdown)
        {
            UpdateState(s => s with
            {
                IsListening = false,
                SensorConnectionState = AntSensorConnectionState.Disconnected,
            });
        }
    }

    private void OnChannelResponse(AntRawMessage message)
    {
        if (disposed || deviceFaulted)
        {
            return;
        }

        AntListeningOptions? currentOptions;
        lock (syncLock)
        {
            currentOptions = activeOptions;
        }

        RawMessageReceived?.Invoke(this, new AntRawMessageReceivedEventArgs(message));

        if (message.IsDataMessageType)
        {
            UpdateState(s => s with
            {
                IsListening = true,
                SensorConnectionState = AntSensorConnectionState.Tracking,
                HasReceivedData = s.HasReceivedData || message.IsDataMessage,
            });

            if (message.IsDataMessage && message.DeviceIdentity is { } identity)
            {
                bool isNew;
                lock (syncLock)
                {
                    isNew = seenDevices.Add((identity.DeviceNumber, identity.DeviceTypeId));
                }
                if (isNew)
                {
                    DeviceDiscovered?.Invoke(this, new AntDeviceDiscoveredEventArgs(identity, message));
                }
            }

            if (message.IsDataMessage && ShouldParseHeartRate(message, currentOptions) && AntHeartRateParser.TryParse(message, out var heartRateData))
            {
                HeartRateReceived?.Invoke(this, new HeartRateDataReceivedEventArgs(heartRateData!, message));
            }

            return;
        }

        if (message.ChannelEventCode is null)
        {
            return;
        }

        switch ((ANT_ReferenceLibrary.ANTEventID)message.ChannelEventCode.Value)
        {
            case ANT_ReferenceLibrary.ANTEventID.EVENT_CHANNEL_ACTIVE_0x0F:
                UpdateState(s => s with
                {
                    IsListening = true,
                    SensorConnectionState = AntSensorConnectionState.Tracking,
                });
                break;

            case ANT_ReferenceLibrary.ANTEventID.EVENT_RX_SEARCH_TIMEOUT_0x01:
            case ANT_ReferenceLibrary.ANTEventID.EVENT_RX_FAIL_GO_TO_SEARCH_0x08:
            case ANT_ReferenceLibrary.ANTEventID.EVENT_RX_FAIL_0x02:
                UpdateState(s => s with
                {
                    IsListening = true,
                    SensorConnectionState = AntSensorConnectionState.Searching,
                });
                break;

            case ANT_ReferenceLibrary.ANTEventID.EVENT_CHANNEL_CLOSED_0x07:
                UpdateState(s => s with
                {
                    IsListening = false,
                    SensorConnectionState = AntSensorConnectionState.Disconnected,
                });
                break;
        }
    }

    private void ConfigureExtendedMessages(AntListeningOptions options)
    {
        if (!options.EnableExtendedMessages || !Device.Capabilities.ExtendedMessaging)
        {
            return;
        }

        device.EnableRxExtendedMessages(true, options.ResponseWaitTimeMs);

        var libConfig = ANT_ReferenceLibrary.LibConfigFlags.MESG_OUT_INC_DEVICE_ID_0x80;
        if (options.IncludeTimestampInMessages)
        {
            libConfig |= ANT_ReferenceLibrary.LibConfigFlags.MESG_OUT_INC_TIME_STAMP_0x20;
        }

        device.SetLibConfig(libConfig, options.ResponseWaitTimeMs);
    }

    private bool ShouldParseHeartRate(AntRawMessage message, AntListeningOptions? options)
    {
        var deviceTypeId = message.DeviceIdentity?.DeviceTypeId ?? options?.ResolvedKnownDeviceTypeId;
        return deviceTypeId == (byte)AntKnownDeviceType.HeartRateMonitor;
    }

    private static AntSensorConnectionState MapSensorConnectionState(ANT_ReferenceLibrary.BasicChannelStatusCode basicStatus)
    {
        return basicStatus switch
        {
            ANT_ReferenceLibrary.BasicChannelStatusCode.SEARCHING_0x2 => AntSensorConnectionState.Searching,
            ANT_ReferenceLibrary.BasicChannelStatusCode.TRACKING_0x3 => AntSensorConnectionState.Tracking,
            _ => AntSensorConnectionState.Unknown,
        };
    }

    private void UpdateState(Func<AntConnectionState, AntConnectionState> transition)
    {
        AntConnectionState previousState;
        AntConnectionState newState;

        lock (syncLock)
        {
            previousState = State;
            newState = transition(previousState);

            if (previousState == newState)
            {
                return;
            }

            State = newState;
        }

        StateChanged?.Invoke(this, new AntConnectionStateChangedEventArgs(previousState, newState));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (deviceFaulted)
        {
            throw new InvalidOperationException(
                "The device connection has encountered an unrecoverable error and must be disposed.");
        }
    }

    private static AntSerialErrorCode ToPublicErrorCode(ANT_Device.serialErrorCode code) => code switch
    {
        ANT_Device.serialErrorCode.SerialWriteError            => AntSerialErrorCode.SerialWriteError,
        ANT_Device.serialErrorCode.SerialReadError             => AntSerialErrorCode.SerialReadError,
        ANT_Device.serialErrorCode.DeviceConnectionLost        => AntSerialErrorCode.DeviceConnectionLost,
        ANT_Device.serialErrorCode.MessageLost_CrcError        => AntSerialErrorCode.MessageLost_CrcError,
        ANT_Device.serialErrorCode.MessageLost_QueueOverflow   => AntSerialErrorCode.MessageLost_QueueOverflow,
        ANT_Device.serialErrorCode.MessageLost_TooLarge        => AntSerialErrorCode.MessageLost_TooLarge,
        ANT_Device.serialErrorCode.MessageLost_InvalidChannel  => AntSerialErrorCode.MessageLost_InvalidChannel,
        _                                                       => AntSerialErrorCode.Unknown,
    };
}
