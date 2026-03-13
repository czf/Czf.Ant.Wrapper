using Czf.Ant.Wrapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTestProject1
{
    [TestClass]
    public class ModelTests
    {
        [TestMethod]
        public void AntRawMessage_IsDataMessage_TrueWhenDataPayloadPresent()
        {
            var message = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], [0x01], null, null, null);
            Assert.IsTrue(message.IsDataMessage);
        }

        [TestMethod]
        public void AntRawMessage_IsDataMessage_FalseWhenDataPayloadNull()
        {
            var message = new AntRawMessage(0, DateTime.UtcNow, 0x40, [], null, null, 0x01, 0x4E);
            Assert.IsFalse(message.IsDataMessage);
        }

        [TestMethod]
        public void AntConnectionState_WithExpression_ProducesNewInstance()
        {
            var original = new AntConnectionState(
                IsDeviceOpen: true,
                IsListening: false,
                SensorConnectionState: AntSensorConnectionState.Unknown,
                HasReceivedData: false);

            var updated = original with { IsListening = true, SensorConnectionState = AntSensorConnectionState.Searching };

            Assert.IsFalse(original.IsListening);
            Assert.IsTrue(updated.IsListening);
            Assert.AreEqual(AntSensorConnectionState.Searching, updated.SensorConnectionState);
            Assert.IsTrue(updated.IsDeviceOpen);
        }

        [TestMethod]
        public void AntConnectionState_ValueEquality()
        {
            var a = new AntConnectionState(true, true, AntSensorConnectionState.Tracking, true);
            var b = new AntConnectionState(true, true, AntSensorConnectionState.Tracking, true);

            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void AntDeviceIdentity_ValueEquality()
        {
            var a = new AntDeviceIdentity(100, 120, 1, false);
            var b = new AntDeviceIdentity(100, 120, 1, false);

            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void AntRawMessage_IsDataMessageType_FalseByDefault()
        {
            var message = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], null, null, null, null);
            Assert.IsFalse(message.IsDataMessageType);
        }

        [TestMethod]
        public void AntRawMessage_IsDataMessageType_DistinguishesFailedExtractionFromNonData()
        {
            // IsDataMessageType=true + DataPayload=null means it WAS a data message but
            // payload extraction failed — different from a non-data message.
            var failed = new AntRawMessage(0, DateTime.UtcNow, 0x4E, [], null, null, null, null)
            {
                IsDataMessageType = true,
            };

            Assert.IsTrue(failed.IsDataMessageType);
            Assert.IsFalse(failed.IsDataMessage);

            var nonData = new AntRawMessage(0, DateTime.UtcNow, 0x40, [], null, null, 0x01, 0x4E);
            Assert.IsFalse(nonData.IsDataMessageType);
            Assert.IsFalse(nonData.IsDataMessage);
        }

        [TestMethod]
        public void AntConnectionStateChangedEventArgs_ExposesStates()
        {
            var prev = new AntConnectionState(true, false, AntSensorConnectionState.Unknown, false);
            var curr = new AntConnectionState(true, true, AntSensorConnectionState.Searching, false);
            var args = new AntConnectionStateChangedEventArgs(prev, curr);

            Assert.AreEqual(prev, args.PreviousState);
            Assert.AreEqual(curr, args.CurrentState);
        }
    }
}
