using Czf.Ant.Wrapper;

// ---------------------------------------------------------------------------
// Czf.Ant.Wrapper demo
//
// Phase 1 — Discover ANT+ Heart Rate Monitor devices nearby using a
//            temporary RX scan mode pass.
// Phase 2 — Open a dedicated slave channel to the chosen HRM.
//
// Also demonstrates:
//   - State change handling including disconnect/range-loss
//   - Error handling for hardware faults (DeviceError)
//   - Raw message access alongside parsed heart rate data
// ---------------------------------------------------------------------------

Console.WriteLine("=== Czf.Ant.Wrapper Demo ===");
Console.WriteLine();

var manager = new AntDeviceManager();

// ── USB dongle discovery ─────────────────────────────────────────────────────

Console.WriteLine("Scanning for ANT USB dongles...");
var dongles = manager.GetAvailableDevices(includeUsbDetails: true);

if (dongles.Count == 0)
{
    Console.WriteLine("No ANT USB dongles detected. Plug one in and try again.");
    return;
}

Console.WriteLine($"Found {dongles.Count} dongle(s):");
foreach (var d in dongles)
{
    if (d.ProbeError is not null)
        Console.WriteLine($"  [{d.UsbDeviceNumber}] probe failed: {d.ProbeError}");
    else
        Console.WriteLine($"  [{d.UsbDeviceNumber}] {d.ProductDescription}  SN={d.SerialNumber}");
}
Console.WriteLine();

// ── Phase 1: scan for nearby HRM sensors ────────────────────────────────────

Console.WriteLine("Phase 1: scanning for nearby ANT+ Heart Rate Monitors...");
Console.WriteLine("(Keep your HRM active. Press any key when you have seen the devices you want.)");
Console.WriteLine();

var discovered = new Dictionary<ushort, HeartRateData>();

try
{
    await using var scanConn = await manager.ConnectFirstAvailableAsync();

    scanConn.DeviceError += OnDeviceError;
    scanConn.HeartRateReceived += OnHrmDiscovered;
    scanConn.DeviceDiscovered += (s, e) =>
    {
        // This event fires for every ANT device found during scanning, including non-HRM devices.
        // We rely on HeartRateReceived to filter for HRMs, but this can be useful for diagnostics.
        var id = e.Identity;
        if (id is null) return;
        Console.WriteLine($"[Scan]   Discovered device #{id.DeviceNumber}  type={id.DeviceTypeId}  tx={id.TransmissionTypeId}");
    };

    //await scanConn.StartHeartRateMonitoringAsync(new AntHeartRateMonitorOptions
    //{
    //    UseRxScanMode = true,
    //    DeviceNumber = 0,
    //});
    scanConn.StartScan(new AntScanOptions
    {
        NetworkKey = AntPlusNetworks.PublicNetworkKey,  // ANT+ devices only
    });
    Console.ReadKey(intercept: true);
    Console.WriteLine();

    scanConn.HeartRateReceived -= OnHrmDiscovered;
    // Disposing resets the dongle, ending scan mode cleanly.
}
catch (Exception ex)
{
    Console.WriteLine($"Error during scan: {ex.Message}");
    return;
}

void OnHrmDiscovered(object? sender, HeartRateDataReceivedEventArgs e)
{
    var id = e.Data.DeviceIdentity;
    if (id is null) return;
    lock (discovered)
    {
        discovered[id.DeviceNumber] = e.Data;
        Console.Write($"\r  Found HRMs: {string.Join(", ", discovered.Keys.Select(k => $"#{k}"))}   ");
    }
}

Console.WriteLine();

if (discovered.Count == 0)
{
    Console.WriteLine("No HRM devices found. Make sure your HRM is active and in range.");
    return;
}

// ── Pick a device ────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("Discovered ANT+ Heart Rate Monitors:");
var list = discovered.Values.ToList();
for (int i = 0; i < list.Count; i++)
{
    var id = list[i].DeviceIdentity!;
    Console.WriteLine($"  [{i}] Device #{id.DeviceNumber}  type={id.DeviceTypeId}  tx={id.TransmissionTypeId}");
}

int choice = 0;
if (list.Count > 1)
{
    Console.Write($"Enter number to connect to [0-{list.Count - 1}]: ");
    if (!int.TryParse(Console.ReadLine(), out choice) || choice < 0 || choice >= list.Count)
        choice = 0;
}

var chosen = list[choice].DeviceIdentity!;
Console.WriteLine($"Connecting to HRM #{chosen.DeviceNumber}...");
Console.WriteLine();

// ── Phase 2: dedicated slave channel to the chosen device ────────────────────

try
{
    await using var conn = await manager.ConnectFirstAvailableAsync();

    conn.StateChanged += OnStateChanged;
    conn.DeviceError += OnDeviceError;
    conn.HeartRateReceived += OnHeartRate;

    // RawMessageReceived fires for every ANT message — useful for diagnostics
    // or handling device profiles not yet parsed by the wrapper.
    conn.RawMessageReceived += OnRawMessage;

    await conn.StartHeartRateMonitoringAsync(new AntHeartRateMonitorOptions
    {
        UseRxScanMode = false,                      // dedicated slave channel
        DeviceNumber = chosen.DeviceNumber,         // pair with this HRM only
        TransmissionTypeId = chosen.TransmissionTypeId,
    });

    Console.WriteLine("Listening. Press any key to stop.");
    Console.WriteLine();
    Console.ReadKey(intercept: true);

    conn.StopListening();
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("Done.");

// ── Handlers ─────────────────────────────────────────────────────────────────

static void OnStateChanged(object? sender, AntConnectionStateChangedEventArgs e)
{
    var s = e.CurrentState;
    // SensorConnectionState transitions: Unknown -> Searching -> Tracking -> Disconnected
    // Disconnected fires when the HRM goes out of range or loses signal.
    Console.WriteLine($"[State]  sensor={s.SensorConnectionState,-12}  listening={s.IsListening}");

    if (e.CurrentState.SensorConnectionState == AntSensorConnectionState.Disconnected)
        Console.WriteLine("         HRM went out of range. Move it closer to resume tracking.");
}

static void OnHeartRate(object? sender, HeartRateDataReceivedEventArgs e)
{
    var hr = e.Data;
    Console.WriteLine($"[HRM]    {hr.ComputedHeartRate,3} bpm   beat #{hr.BeatCount,-5}  page={hr.PageNumber}");
}

static void OnRawMessage(object? sender, AntRawMessageReceivedEventArgs e)
{
    var msg = e.Message;
    // Only log data messages to avoid spamming channel-event traffic.
    if (!msg.IsDataMessage) return;
    var hex = BitConverter.ToString(msg.DataPayload!).Replace("-", " ");
    Console.WriteLine($"[Raw]    ch={msg.ChannelNumber}  responseId=0x{msg.ResponseId:X2}  payload={hex}");
}

static void OnDeviceError(object? sender, AntDeviceErrorEventArgs e)
{
    Console.WriteLine($"[Error]  code={e.ErrorCode}  critical={e.IsCritical}");
    if (e.IsCritical)
        Console.WriteLine("         Device connection lost. Dispose and reconnect to recover.");
}
