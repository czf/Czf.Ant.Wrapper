using Czf.Ant.Wrapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTestProject1
{
    [TestClass]
    public class HeartRateParserTests
    {
        private static readonly DateTime FixedTime = new(2024, 1, 1);

        // ---- payload length guards ----

        [TestMethod]
        public void TryParse_CorrectPayload_ReturnsTrue()
        {
            var payload = new byte[] { 0x80, 0x00, 0x00, 0x00, 0x34, 0x12, 0x56, 0x78 };
            Assert.IsTrue(AntHeartRateParser.TryParse(payload, null, FixedTime, out _));
        }

        [TestMethod]
        public void TryParse_ShortPayload_ReturnsFalse()
        {
            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
            Assert.IsFalse(AntHeartRateParser.TryParse(payload, null, FixedTime, out var data));
            Assert.IsNull(data);
        }

        [TestMethod]
        public void TryParse_LongPayload_ReturnsFalse()
        {
            var payload = new byte[9];
            Assert.IsFalse(AntHeartRateParser.TryParse(payload, null, FixedTime, out _));
        }

        [TestMethod]
        public void TryParse_EmptyPayload_ReturnsFalse()
        {
            Assert.IsFalse(AntHeartRateParser.TryParse([], null, FixedTime, out _));
        }

        // ---- data extraction correctness ----

        [TestMethod]
        public void TryParse_ParsesComputedHeartRate()
        {
            var payload = new byte[] { 0x80, 0x00, 0x00, 0x00, 0x34, 0x12, 0x56, 0x78 };
            AntHeartRateParser.TryParse(payload, null, FixedTime, out var data);

            Assert.AreEqual(0x78, data!.ComputedHeartRate);
            Assert.AreEqual(0x56, data.BeatCount);
        }

        [TestMethod]
        public void TryParse_BeatTime_LittleEndian()
        {
            // bytes[4] = 0x34, bytes[5] = 0x12  →  0x1234
            var payload = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x34, 0x12, 0x00, 0x00 };
            AntHeartRateParser.TryParse(payload, null, FixedTime, out var data);

            Assert.AreEqual((ushort)0x1234, data!.BeatTime);
        }

        [TestMethod]
        public void TryParse_ToggleBitTrue_WhenHighBitSet()
        {
            var payload = new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            AntHeartRateParser.TryParse(payload, null, FixedTime, out var data);

            Assert.IsTrue(data!.ToggleBitSet);
            Assert.AreEqual(0, data.PageNumber);
        }

        [TestMethod]
        public void TryParse_ToggleBitFalse_WhenHighBitClear()
        {
            var payload = new byte[] { 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            AntHeartRateParser.TryParse(payload, null, FixedTime, out var data);

            Assert.IsFalse(data!.ToggleBitSet);
        }

        [TestMethod]
        public void TryParse_PageNumber_MasksHighBit()
        {
            // 0xFF → toggle=true, page=127
            var payload = new byte[] { 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            AntHeartRateParser.TryParse(payload, null, FixedTime, out var data);

            Assert.AreEqual(127, data!.PageNumber);
            Assert.IsTrue(data.ToggleBitSet);
        }

        [TestMethod]
        public void TryParse_DeviceIdentity_Preserved()
        {
            var identity = new AntDeviceIdentity(42, 120, 1, false);
            var payload = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x60 };
            AntHeartRateParser.TryParse(payload, identity, FixedTime, out var data);

            Assert.AreEqual(identity, data!.DeviceIdentity);
        }

        [TestMethod]
        public void TryParse_Timestamp_Preserved()
        {
            var payload = new byte[8];
            AntHeartRateParser.TryParse(payload, null, FixedTime, out var data);

            Assert.AreEqual(FixedTime, data!.Timestamp);
        }

        // ---- AntRawMessage overload ----

        [TestMethod]
        public void TryParse_RawMessage_NullDataPayload_ReturnsFalse()
        {
            var message = new AntRawMessage(0, FixedTime, 0x4E, [], null, null, null, null);
            Assert.IsFalse(AntHeartRateParser.TryParse(message, out _));
        }

        [TestMethod]
        public void TryParse_RawMessage_WithValidPayload_ReturnsTrue()
        {
            var payload = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x60 };
            var message = new AntRawMessage(0, FixedTime, 0x4E, payload, payload, null, null, null);
            Assert.IsTrue(AntHeartRateParser.TryParse(message, out var data));
            Assert.AreEqual(0x60, data!.ComputedHeartRate);
        }
    }
}
