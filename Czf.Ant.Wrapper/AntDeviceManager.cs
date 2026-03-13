using ANT_Managed_Library;

namespace Czf.Ant.Wrapper;

/// <summary>
/// Entry point for ANT USB device discovery and connection management.
/// Use <see cref="GetAvailableDevices"/> to enumerate connected dongles and
/// <see cref="Connect"/> or <see cref="ConnectFirstAvailable"/> to open an
/// <see cref="AntDeviceConnection"/>.
/// </summary>
public sealed class AntDeviceManager
{
    private static readonly uint[] DefaultBaudRates = [57600, 50000];

    /// <summary>
    /// Returns the number of ANT USB dongles detected by the ANT runtime.
    /// </summary>
    public uint GetDetectedDeviceCount()
    {
        ANT_Common.checkUnmanagedLibrary();
        return ANT_Common.getNumDetectedUSBDevices();
    }

    /// <summary>
    /// Returns a snapshot of all detected ANT USB dongles.
    /// </summary>
    /// <param name="includeUsbDetails">
    /// When <c>true</c>, temporarily opens each device to populate USB product description,
    /// serial string, serial number, VID, and PID. This is slower but gives richer metadata.
    /// When <c>false</c> (default), only the USB device number is populated.
    /// </param>
    public IReadOnlyList<AntAvailableDevice> GetAvailableDevices(bool includeUsbDetails = false)
    {
        ANT_Common.checkUnmanagedLibrary();

        var count = ANT_Common.getNumDetectedUSBDevices();
        var devices = new List<AntAvailableDevice>((int)count);

        for (byte deviceNumber = 0; deviceNumber < count; deviceNumber++)
        {
            devices.Add(includeUsbDetails
                ? ProbeDevice(deviceNumber)
                : new AntAvailableDevice(deviceNumber));
        }

        return devices;
    }

    /// <summary>
    /// Asynchronous wrapper around <see cref="GetAvailableDevices"/>. Enumeration is synchronous;
    /// this completes immediately.
    /// </summary>
    /// <param name="includeUsbDetails">See <see cref="GetAvailableDevices"/>.</param>
    /// <param name="cancellationToken">Checked before enumerating.</param>
    public Task<IReadOnlyList<AntAvailableDevice>> GetAvailableDevicesAsync(bool includeUsbDetails = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetAvailableDevices(includeUsbDetails));
    }

    /// <summary>
    /// Opens a connection to the ANT USB dongle at <paramref name="usbDeviceNumber"/> and returns
    /// a ready-to-use <see cref="AntDeviceConnection"/>. The caller is responsible for disposing
    /// the returned connection.
    /// </summary>
    /// <param name="usbDeviceNumber">Zero-based USB device index (see <see cref="GetAvailableDevices"/>).</param>
    /// <param name="baudRate">Baud rate to use; 57 600 works for most dongles.</param>
    /// <exception cref="ANT_Managed_Library.ANT_Exception">The device could not be opened at the specified baud rate.</exception>
    public AntDeviceConnection Connect(byte usbDeviceNumber, uint baudRate = 57600)
    {
        var antDevice = new ANT_Device(usbDeviceNumber, baudRate);
        var snapshot = CreateDeviceSnapshot(antDevice, usbDeviceNumber, baudRate);
        var adapter = new AntDeviceAdapter(antDevice);
        return new AntDeviceConnection(adapter, snapshot);
    }

    /// <summary>
    /// Asynchronous wrapper around <see cref="Connect"/>. The device is opened synchronously;
    /// this completes immediately.
    /// </summary>
    /// <param name="usbDeviceNumber">Zero-based USB device index.</param>
    /// <param name="baudRate">Baud rate to use; 57 600 works for most dongles.</param>
    /// <param name="cancellationToken">Checked before connecting.</param>
    public Task<AntDeviceConnection> ConnectAsync(byte usbDeviceNumber, uint baudRate = 57600, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Connect(usbDeviceNumber, baudRate));
    }

    /// <summary>
    /// Opens the first detected ANT USB dongle, trying common baud rates automatically.
    /// </summary>
    /// <returns>A ready-to-use <see cref="AntDeviceConnection"/>; caller must dispose it.</returns>
    /// <exception cref="ANT_Managed_Library.ANT_Exception">No devices were detected, or none could be opened.</exception>
    public AntDeviceConnection ConnectFirstAvailable()
    {
        ANT_Common.checkUnmanagedLibrary();

        var count = ANT_Common.getNumDetectedUSBDevices();
        if (count == 0)
        {
            throw new ANT_Exception("No ANT devices detected.");
        }

        for (byte deviceNumber = 0; deviceNumber < count; deviceNumber++)
        {
            foreach (var baudRate in DefaultBaudRates)
            {
                try
                {
                    return Connect(deviceNumber, baudRate);
                }
                catch (ANT_Exception)
                {
                }
            }
        }

        throw new ANT_Exception("Failed to connect to a detected ANT device.");
    }

    /// <summary>
    /// Asynchronous wrapper around <see cref="ConnectFirstAvailable"/>.
    /// </summary>
    /// <param name="cancellationToken">Checked before connecting.</param>
    public Task<AntDeviceConnection> ConnectFirstAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ConnectFirstAvailable());
    }

    private static AntAvailableDevice ProbeDevice(byte usbDeviceNumber)
    {
        foreach (var baudRate in DefaultBaudRates)
        {
            try
            {
                using var device = new ANT_Device(usbDeviceNumber, baudRate);
                return CreateAvailableDevice(device, usbDeviceNumber, baudRate);
            }
            catch (ANT_Exception ex)
            {
                if (baudRate == DefaultBaudRates[^1])
                {
                    return new AntAvailableDevice(usbDeviceNumber, ProbeError: ex.Message);
                }
            }
        }

        return new AntAvailableDevice(usbDeviceNumber, ProbeError: "Unable to probe device.");
    }

    private static AntAvailableDevice CreateAvailableDevice(ANT_Device device, byte usbDeviceNumber, uint baudRate)
    {
        var usbInfo = device.getDeviceUSBInfo();

        return new AntAvailableDevice(
            usbDeviceNumber,
            baudRate,
            usbInfo.printProductDescription(),
            usbInfo.printSerialString(),
            device.getSerialNumber(),
            device.getDeviceUSBVID(),
            device.getDeviceUSBPID(),
            ProbeError: null);
    }

    private static AntDeviceSnapshot CreateDeviceSnapshot(ANT_Device device, byte usbDeviceNumber, uint baudRate)
    {
        var usbInfo = device.getDeviceUSBInfo();
        var capabilities = device.getDeviceCapabilities();

        return new AntDeviceSnapshot(
            usbDeviceNumber,
            baudRate,
            device.getSerialNumber(),
            device.getDeviceUSBVID(),
            device.getDeviceUSBPID(),
            usbInfo.printProductDescription(),
            usbInfo.printSerialString(),
            new AntDeviceCapabilitiesSnapshot(
                capabilities.maxANTChannels,
                capabilities.maxNetworks,
                capabilities.maxDataChannels,
                capabilities.ExtendedMessaging,
                capabilities.ScanModeSupport,
                capabilities.SerialNumber,
                capabilities.SearchList,
                capabilities.SearchSharing,
                capabilities.HighDutySearch));
    }
}

