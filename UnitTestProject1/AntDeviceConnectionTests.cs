using ANT_Managed_Library;
using Czf.Ant.Wrapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTestProject1
{
    [TestClass]
    public class AntDeviceConnectionTests
    {
        // ---- fakes ----

        private sealed class FakeAntChannel : IAntChannel
        {
            private readonly System.Threading.ManualResetEventSlim _assignedSignal = new(false);
            private readonly System.Threading.ManualResetEventSlim _openGate = new(true);

            public bool CloseChannelCalled;
            public bool UnassignChannelCalled;
            public bool BlockOnOpen;
            public ANT_ReferenceLibrary.BasicChannelStatusCode StatusToReturn =
                ANT_ReferenceLibrary.BasicChannelStatusCode.SEARCHING_0x2;

            public event Action<ANT_Device.DeviceNotificationCode, object>? DeviceNotification;
            public event Action<AntRawMessage>? ChannelResponse;

            public void FireChannelResponse(AntRawMessage message) => ChannelResponse?.Invoke(message);
            public void FireDeviceNotification(ANT_Device.DeviceNotificationCode code)
                => DeviceNotification?.Invoke(code, null!);

            /// <summary>Blocks <see cref="OpenChannel"/> until <see cref="Release"/> is called.</summary>
            public void BlockOpen() { _openGate.Reset(); BlockOnOpen = true; }
            public void Release() => _openGate.Set();

            /// <summary>Waits until <see cref="AssignChannel"/> has been called (past the sync lock).</summary>
            public void WaitForAssign(int timeoutMs = 2000) => _assignedSignal.Wait(timeoutMs);

            public void AssignChannel(ANT_ReferenceLibrary.ChannelType _, byte __, uint ___)
                => _assignedSignal.Set();
            public void SetChannelId(ushort a, bool b, byte c, byte d, uint e) { }
            public void SetChannelFreq(byte a, uint b) { }
            public void SetChannelPeriod(ushort a, uint b) { }
            public void SetChannelSearchTimeout(byte a, uint b) { }
            public void SetLowPrioritySearchTimeout(byte a, uint b) { }
            public void OpenChannel(uint _) { if (BlockOnOpen) _openGate.Wait(); }
            public void CloseChannel(uint _) => CloseChannelCalled = true;
            public void UnassignChannel(uint _) => UnassignChannelCalled = true;
            public ANT_ReferenceLibrary.BasicChannelStatusCode RequestStatus(uint _) => StatusToReturn;
        }

        private sealed class FakeAntDevice : IAntDevice
        {
            public FakeAntChannel Channel { get; } = new FakeAntChannel();
            public bool OpenRxScanModeThrows;
            public bool Disposed;

            public event Action<ANT_Device.serialErrorCode, bool>? SerialError;

            public void FireSerialError(ANT_Device.serialErrorCode code, bool isCritical)
                => SerialError?.Invoke(code, isCritical);

            public void SetNetworkKey(byte a, byte[] b, uint c) { }
            public void EnableRxExtendedMessages(bool a, uint b) { }
            public void SetLibConfig(ANT_ReferenceLibrary.LibConfigFlags a, uint b) { }
            public IAntChannel GetChannel(byte _) => Channel;
            public void OpenRxScanMode(uint _)
            {
                if (OpenRxScanModeThrows) throw new Exception("scan mode failed");
            }
            public void Dispose() => Disposed = true;
        }

        // ---- helpers ----

        private static AntDeviceSnapshot BuildSnapshot(bool scanModeSupport = true) =>
            new AntDeviceSnapshot(
                0, 57600, 0, 0, 0, "Fake", "Fake",
                new AntDeviceCapabilitiesSnapshot(8, 3, 0, false, scanModeSupport, false, false, false, false));

        private static AntDeviceConnection BuildConnection(FakeAntDevice device, bool scanModeSupport = true)
            => new AntDeviceConnection(device, BuildSnapshot(scanModeSupport));

        private static AntListeningOptions ScanModeOptions() => new AntListeningOptions { UseRxScanMode = true };
        private static AntListeningOptions NormalModeOptions() => new AntListeningOptions { UseRxScanMode = false };

        // ---- scan mode support guard ----

        [TestMethod]
        public void StartListening_ScanModeNotSupported_Throws()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device, scanModeSupport: false);

            Assert.ThrowsException<InvalidOperationException>(
                () => conn.StartListening(ScanModeOptions()));
        }

        // ---- scan mode initial state ----

        [TestMethod]
        public void StartListening_ScanMode_SetsSearchingState()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);

            conn.StartListening(ScanModeOptions());

            Assert.IsTrue(conn.State.IsListening);
            Assert.AreEqual(AntSensorConnectionState.Searching, conn.State.SensorConnectionState);
        }

        // ---- scan mode stop does not call close/unassign ----

        [TestMethod]
        public void StopListening_AfterScanMode_DoesNotCloseOrUnassignChannel()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);

            conn.StartListening(ScanModeOptions());
            conn.StopListening();

            Assert.IsFalse(device.Channel.CloseChannelCalled);
            Assert.IsFalse(device.Channel.UnassignChannelCalled);
        }

        // ---- normal mode stop calls close/unassign ----

        [TestMethod]
        public void StopListening_AfterNormalMode_ClosesAndUnassignsChannel()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);

            conn.StartListening(NormalModeOptions());
            conn.StopListening();

            Assert.IsTrue(device.Channel.CloseChannelCalled);
            Assert.IsTrue(device.Channel.UnassignChannelCalled);
        }

        // ---- openRxScanMode failure faults the connection ----

        [TestMethod]
        public void StartListening_OpenRxScanModeThrows_FaultsConnection()
        {
            var device = new FakeAntDevice { OpenRxScanModeThrows = true };
            using var conn = BuildConnection(device);

            Assert.ThrowsException<Exception>(() => conn.StartListening(ScanModeOptions()));
            Assert.ThrowsException<InvalidOperationException>(() => conn.StartListening(ScanModeOptions()));
        }

        // ---- concurrent StartListening is blocked ----

        [TestMethod]
        public void StartListening_ConcurrentCall_SecondCallThrows()
        {
            var device = new FakeAntDevice();
            device.Channel.BlockOpen();
            using var conn = BuildConnection(device, scanModeSupport: false);

            Exception? secondCallException = null;
            var thread1 = new System.Threading.Thread(() =>
            {
                try { conn.StartListening(NormalModeOptions()); } catch { }
            });
            var thread2 = new System.Threading.Thread(() =>
            {
                device.Channel.WaitForAssign();
                try { conn.StartListening(NormalModeOptions()); }
                catch (Exception ex) { secondCallException = ex; }
            });

            thread1.Start();
            thread2.Start();
            device.Channel.WaitForAssign();
            device.Channel.Release();
            thread1.Join(3000);
            thread2.Join(3000);

            Assert.IsInstanceOfType(secondCallException, typeof(InvalidOperationException));
        }

        // ---- critical serial error cleans up state ----

        [TestMethod]
        public void CriticalSerialError_SetsDisconnectedState()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(ScanModeOptions());

            device.FireSerialError(ANT_Device.serialErrorCode.DeviceConnectionLost, isCritical: true);

            Assert.IsFalse(conn.State.IsDeviceOpen);
            Assert.IsFalse(conn.State.IsListening);
            Assert.AreEqual(AntSensorConnectionState.Disconnected, conn.State.SensorConnectionState);
        }

        [TestMethod]
        public void CriticalSerialError_SubsequentChannelEvent_DoesNotChangeState()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(NormalModeOptions());

            device.FireSerialError(ANT_Device.serialErrorCode.DeviceConnectionLost, isCritical: true);

            var stateAfterError = conn.State;

            // Channel response after fault should be silently ignored.
            var dataMsg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], new byte[8], null, null, null)
            {
                IsDataMessageType = true,
            };
            device.Channel.FireChannelResponse(dataMsg);

            Assert.AreEqual(stateAfterError, conn.State);
        }

        // ---- channel response state machine ----

        [TestMethod]
        public void ChannelResponse_DataMessage_SetsTracking()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(NormalModeOptions());

            var msg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], new byte[8], null, null, null)
            {
                IsDataMessageType = true,
            };
            device.Channel.FireChannelResponse(msg);

            Assert.AreEqual(AntSensorConnectionState.Tracking, conn.State.SensorConnectionState);
            Assert.IsTrue(conn.State.HasReceivedData);
        }

        [TestMethod]
        public void ChannelResponse_DataMessageWithFailedPayload_StillSetsTracking()
        {
            // IsDataMessageType=true with DataPayload=null (payload extraction failed).
            // The state machine should still move to Tracking since the message type IS a data type.
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(NormalModeOptions());

            var msg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], null, null, null, null)
            {
                IsDataMessageType = true,
            };
            device.Channel.FireChannelResponse(msg);

            Assert.AreEqual(AntSensorConnectionState.Tracking, conn.State.SensorConnectionState);
        }

        [TestMethod]
        public void ChannelResponse_SearchTimeout_SetsSearching()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(NormalModeOptions());

            // First get to Tracking so we have a meaningful state transition.
            var dataMsg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], new byte[8], null, null, null)
            { IsDataMessageType = true };
            device.Channel.FireChannelResponse(dataMsg);

            var timeoutMsg = new AntRawMessage(
                0, DateTime.UtcNow, 0x40, [],
                null, null,
                (byte)ANT_ReferenceLibrary.ANTEventID.EVENT_RX_SEARCH_TIMEOUT_0x01,
                (byte)ANT_ReferenceLibrary.ANTMessageID.ACKNOWLEDGED_DATA_0x4F);
            device.Channel.FireChannelResponse(timeoutMsg);

            Assert.AreEqual(AntSensorConnectionState.Searching, conn.State.SensorConnectionState);
            Assert.IsTrue(conn.State.IsListening);
        }

        [TestMethod]
        public void ChannelResponse_ChannelClosed_SetsNotListening()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(NormalModeOptions());

            var closedMsg = new AntRawMessage(
                0, DateTime.UtcNow, 0x40, [],
                null, null,
                (byte)ANT_ReferenceLibrary.ANTEventID.EVENT_CHANNEL_CLOSED_0x07,
                (byte)ANT_ReferenceLibrary.ANTMessageID.ACKNOWLEDGED_DATA_0x4F);
            device.Channel.FireChannelResponse(closedMsg);

            Assert.IsFalse(conn.State.IsListening);
            Assert.AreEqual(AntSensorConnectionState.Disconnected, conn.State.SensorConnectionState);
        }

        // ---- device notification ----

        [TestMethod]
        public void DeviceNotification_Shutdown_SetsDisconnected()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(NormalModeOptions());

            device.Channel.FireDeviceNotification(ANT_Device.DeviceNotificationCode.Shutdown);

            Assert.IsFalse(conn.State.IsListening);
            Assert.AreEqual(AntSensorConnectionState.Disconnected, conn.State.SensorConnectionState);
        }

        // ---- HRM profile event ----

        [TestMethod]
        public void ChannelResponse_HrmData_FiresHeartRateReceivedEvent()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(new AntListeningOptions
            {
                UseRxScanMode = false,
                DeviceTypeId = 120,
                KnownDeviceTypeId = 120,
            });

            HeartRateDataReceivedEventArgs? receivedArgs = null;
            conn.HeartRateReceived += (_, e) => receivedArgs = e;

            // 8-byte broadcast HRM payload: page 0, heart rate = 72
            var hrmPayload = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x48, 0x00, 0x00, 72 };
            var msg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], hrmPayload, null, null, null)
            {
                IsDataMessageType = true,
            };
            device.Channel.FireChannelResponse(msg);

            Assert.IsNotNull(receivedArgs);
            Assert.AreEqual(72, receivedArgs.Data.ComputedHeartRate);
        }

        // ---- DeviceDiscovered event ----

        [TestMethod]
        public void DeviceDiscovered_FirstMessage_Fires()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(ScanModeOptions());

            AntDeviceDiscoveredEventArgs? discoveredArgs = null;
            conn.DeviceDiscovered += (_, e) => discoveredArgs = e;

            var identity = new AntDeviceIdentity(1234, 120, 1, false);
            var msg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], new byte[8], identity, null, null)
            {
                IsDataMessageType = true,
            };
            device.Channel.FireChannelResponse(msg);

            Assert.IsNotNull(discoveredArgs);
            Assert.AreEqual(1234, discoveredArgs.Identity.DeviceNumber);
            Assert.AreEqual(120, discoveredArgs.Identity.DeviceTypeId);
        }

        [TestMethod]
        public void DeviceDiscovered_SameDeviceTwice_FiresOnce()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(ScanModeOptions());

            int fireCount = 0;
            conn.DeviceDiscovered += (_, _) => fireCount++;

            var identity = new AntDeviceIdentity(1234, 120, 1, false);
            var msg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], new byte[8], identity, null, null)
            {
                IsDataMessageType = true,
            };
            device.Channel.FireChannelResponse(msg);
            device.Channel.FireChannelResponse(msg);
            device.Channel.FireChannelResponse(msg);

            Assert.AreEqual(1, fireCount);
        }

        [TestMethod]
        public void DeviceDiscovered_TwoDifferentDevices_FiresTwice()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);
            conn.StartListening(ScanModeOptions());

            int fireCount = 0;
            conn.DeviceDiscovered += (_, _) => fireCount++;

            var id1 = new AntDeviceIdentity(1111, 120, 1, false);
            var id2 = new AntDeviceIdentity(2222, 11, 1, false);
            var payload = new byte[8];
            device.Channel.FireChannelResponse(new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], payload, id1, null, null) { IsDataMessageType = true });
            device.Channel.FireChannelResponse(new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], payload, id2, null, null) { IsDataMessageType = true });

            Assert.AreEqual(2, fireCount);
        }

        [TestMethod]
        public void DeviceDiscovered_ResetsOnNewStartListening()
        {
            var device = new FakeAntDevice();
            using var conn = BuildConnection(device);

            var identity = new AntDeviceIdentity(1234, 120, 1, false);
            var payload = new byte[8];
            var msg = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], payload, identity, null, null) { IsDataMessageType = true };

            // First session — see the device once
            int fireCount = 0;
            conn.DeviceDiscovered += (_, _) => fireCount++;
            conn.StartListening(ScanModeOptions());
            device.Channel.FireChannelResponse(msg);
            Assert.AreEqual(1, fireCount);

            // Stop and start a new session — same device should be "discovered" again
            conn.StopListening();
            conn.StartListening(ScanModeOptions());
            device.Channel.FireChannelResponse(msg);
            Assert.AreEqual(2, fireCount);
        }

        [TestMethod]
        public void StartScan_UsesDefaultAntPlusKey()
        {
            var options = new AntScanOptions();
            var listening = options.ToListeningOptions();

            CollectionAssert.AreEqual(AntPlusNetworks.PublicNetworkKey, listening.NetworkKey);
            Assert.AreEqual(AntPlusNetworks.StandardRadioFrequency, listening.RadioFrequency);
            Assert.AreEqual(0, listening.DeviceTypeId);
            Assert.IsTrue(listening.UseRxScanMode);
        }
    }
}
