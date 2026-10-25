using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Xrpl.Keypairs.Tests
{
    [TestClass]
    public class TestURipemd160
    {
        // Test vector from the RIPEMD-160 reference page: eight repetitions of "1234567890" (80 bytes).
        private const string Message = "12345678901234567890123456789012345678901234567890123456789012345678901234567890";
        private const string Expected = "9b752e45573d4b39f4dbd3323cab82bf63326bfb";

        [TestMethod]
        public void ComputeHash_WholeBuffer_MatchesReferenceVector()
        {
            using RIPEMD160Managed ripemd = new RIPEMD160Managed();
            byte[] hash = ripemd.ComputeHash(Encoding.ASCII.GetBytes(Message));
            Assert.AreEqual(Expected, Convert.ToHexString(hash).ToLowerInvariant());
        }

        [TestMethod]
        public void ComputeHash_WithOffset_HashesOnlyTheRequestedRange()
        {
            // 80 bytes take the full-block path for the first 64; a nonzero offset must shift that read too.
            byte[] message = Encoding.ASCII.GetBytes(Message);
            byte[] buffer = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }.Concat(message).Concat(new byte[] { 0xFF, 0xFF }).ToArray();

            using RIPEMD160Managed ripemd = new RIPEMD160Managed();
            byte[] hash = ripemd.ComputeHash(buffer, 5, message.Length);
            Assert.AreEqual(Expected, Convert.ToHexString(hash).ToLowerInvariant());
        }
    }
}
