using Czf.Ant.Wrapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTestProject1
{
    [TestClass]
    public class ListeningOptionsValidationTests
    {
        [TestMethod]
        public void Validate_NullNetworkKey_DoesNotThrow()
        {
            var options = new AntListeningOptions { NetworkKey = null };
            options.Validate();
        }

        [TestMethod]
        public void Validate_EightByteKey_DoesNotThrow()
        {
            var options = new AntListeningOptions
            {
                NetworkKey = [0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45]
            };
            options.Validate();
        }

        [TestMethod]
        public void Validate_ThreeByteKey_ThrowsArgumentException()
        {
            var options = new AntListeningOptions { NetworkKey = [0x01, 0x02, 0x03] };
            Assert.ThrowsException<ArgumentException>(() => options.Validate());
        }

        [TestMethod]
        public void Validate_EmptyNetworkKey_ThrowsArgumentException()
        {
            var options = new AntListeningOptions { NetworkKey = [] };
            Assert.ThrowsException<ArgumentException>(() => options.Validate());
        }

        [TestMethod]
        public void Validate_ResponseWaitTimeZero_ThrowsArgumentOutOfRangeException()
        {
            var options = new AntListeningOptions { ResponseWaitTimeMs = 0 };
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [TestMethod]
        public void Validate_ResponseWaitTimeOne_DoesNotThrow()
        {
            var options = new AntListeningOptions { ResponseWaitTimeMs = 1 };
            options.Validate();
        }
    }
}
