using Czf.Ant.Wrapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTestProject1
{
    [TestClass]
    public class HeartRateMonitorOptionsTests
    {
        [TestMethod]
        public void ToListeningOptions_UsesExpectedDefaults()
        {
            var options = new AntHeartRateMonitorOptions();
            var listening = options.ToListeningOptions();

            Assert.AreEqual((byte)AntKnownDeviceType.HeartRateMonitor, listening.DeviceTypeId);
            Assert.AreEqual((byte)AntKnownDeviceType.HeartRateMonitor, listening.KnownDeviceTypeId);
            Assert.AreEqual(57, listening.RadioFrequency);
            Assert.AreEqual((ushort)8070, listening.ChannelPeriod);
            Assert.IsTrue(listening.UseRxScanMode);
            CollectionAssert.AreEqual(AntHeartRateMonitorOptions.PublicNetworkKey, listening.NetworkKey);
        }

        [TestMethod]
        public void ToListeningOptions_PreservesCustomValues()
        {
            var key = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            var options = new AntHeartRateMonitorOptions
            {
                ChannelNumber = 2,
                NetworkNumber = 1,
                NetworkKey = key,
                DeviceNumber = 1234,
                PairingEnabled = true,
                SearchTimeout = 10,
                UseRxScanMode = false,
                EnableExtendedMessages = false,
                IncludeTimestampInMessages = false,
                ResponseWaitTimeMs = 1000,
            };
            var listening = options.ToListeningOptions();

            Assert.AreEqual(2, listening.ChannelNumber);
            Assert.AreEqual(1, listening.NetworkNumber);
            Assert.AreEqual(key, listening.NetworkKey);
            Assert.AreEqual((ushort)1234, listening.DeviceNumber);
            Assert.IsTrue(listening.PairingEnabled);
            Assert.AreEqual(10, listening.SearchTimeout);
            Assert.IsFalse(listening.UseRxScanMode);
            Assert.IsFalse(listening.EnableExtendedMessages);
            Assert.IsFalse(listening.IncludeTimestampInMessages);
            Assert.AreEqual((uint)1000, listening.ResponseWaitTimeMs);
        }

        [TestMethod]
        public void ToListeningOptions_DeviceTypeId_AlwaysHeartRateMonitor()
        {
            // DeviceTypeId must always be HRM regardless of other settings
            var options = new AntHeartRateMonitorOptions();
            var listening = options.ToListeningOptions();

            Assert.AreEqual((byte)AntKnownDeviceType.HeartRateMonitor, listening.DeviceTypeId);
            Assert.AreEqual((byte)AntKnownDeviceType.HeartRateMonitor, listening.KnownDeviceTypeId);
            Assert.AreEqual(listening.DeviceTypeId, listening.ResolvedKnownDeviceTypeId);
        }
    }
}
