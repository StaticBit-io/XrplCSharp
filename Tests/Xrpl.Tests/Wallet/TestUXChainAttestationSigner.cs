using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Wallet
{
    /// <summary>
    /// The attestation message a witness signs is a canonical STObject with no hash
    /// prefix (rippled AttestationClaim::message / AttestationCreateAccount::message).
    /// The layout tests spell the expected bytes out field by field from the XRPL binary
    /// format, independent of the SDK's codec, so a codec regression on STXChainBridge or
    /// on field ordering shows up here rather than as temXCHAIN_BAD_PROOF on a node.
    /// </summary>
    [TestClass]
    public class TestUXChainAttestationSigner
    {
        private const string LockingDoor = "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH";
        private const string IssuingDoor = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
        private const string Source = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";
        private const string Reward = "rrrrrrrrrrrrrrrrrrrrBZbvji";
        private const string Destination = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";

        private static XChainBridgeModel XrpBridge() => new XChainBridgeModel
        {
            LockingChainDoor = LockingDoor,
            LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
            IssuingChainDoor = IssuingDoor,
            IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
        };

        private static IEnumerable<byte> AccountField(byte fieldCode, string address)
        {
            // type AccountID = 8 (< 16), field code < 16: one header byte 0x8n
            yield return (byte)(0x80 | fieldCode);
            yield return 0x14;
            foreach (byte b in XrplCodec.DecodeAccountID(address)) yield return b;
        }

        private static IEnumerable<byte> AccountFieldWide(byte fieldCode, string address)
        {
            // type AccountID = 8 (< 16), field code >= 16: 0x80 then the field code
            yield return 0x80;
            yield return fieldCode;
            yield return 0x14;
            foreach (byte b in XrplCodec.DecodeAccountID(address)) yield return b;
        }

        private static IEnumerable<byte> XrpAmount(byte typeHeaderFirst, byte? typeHeaderSecond, ulong drops)
        {
            yield return typeHeaderFirst;
            if (typeHeaderSecond is { } second) yield return second;
            ulong encoded = 0x4000000000000000UL | drops;
            for (int shift = 56; shift >= 0; shift -= 8) yield return (byte)(encoded >> shift);
        }

        private static IEnumerable<byte> UInt64Field(byte fieldCode, ulong value)
        {
            // type UInt64 = 3 (< 16), field code >= 16: 0x30 then the field code
            yield return 0x30;
            yield return fieldCode;
            for (int shift = 56; shift >= 0; shift -= 8) yield return (byte)(value >> shift);
        }

        private static IEnumerable<byte> XrpXrpBridge()
        {
            // type XChainBridge = 25 (>= 16), field code 1 (< 16): field code then the type
            yield return 0x01;
            yield return 0x19;
            yield return 0x14;
            foreach (byte b in XrplCodec.DecodeAccountID(LockingDoor)) yield return b;
            foreach (byte b in new byte[20]) yield return b; // XRP issue: 160-bit zero currency, no issuer
            yield return 0x14;
            foreach (byte b in XrplCodec.DecodeAccountID(IssuingDoor)) yield return b;
            foreach (byte b in new byte[20]) yield return b;
        }

        [TestMethod]
        public void TestUClaimMessage_MatchesRippledLayout()
        {
            byte[] message = XChainAttestationSigner.ClaimMessage(
                XrpBridge(), Source, new Currency { ValueAsXrp = 1m }, Reward, wasLockingChainSend: true, "1", Destination);

            List<byte> expected = new List<byte>();
            expected.AddRange(UInt64Field(20, 1));                                   // XChainClaimID
            expected.AddRange(XrpAmount(0x61, null, 1_000_000));                     // Amount (type 6, field 1)
            expected.AddRange(AccountField(3, Destination));                         // Destination
            expected.AddRange(AccountFieldWide(18, Source));                         // OtherChainSource
            expected.AddRange(AccountFieldWide(21, Reward));                         // AttestationRewardAccount
            expected.AddRange(new byte[] { 0x00, 0x10, 0x13, 0x01 });                // WasLockingChainSend (UInt8, field 19)
            expected.AddRange(XrpXrpBridge());                                       // XChainBridge

            Assert.AreEqual(Convert.ToHexString(expected.ToArray()), Convert.ToHexString(message));
        }

        [TestMethod]
        public void TestUClaimMessage_WithoutDestination_OmitsTheField()
        {
            byte[] withDestination = XChainAttestationSigner.ClaimMessage(
                XrpBridge(), Source, new Currency { ValueAsXrp = 1m }, Reward, false, "1", Destination);
            byte[] without = XChainAttestationSigner.ClaimMessage(
                XrpBridge(), Source, new Currency { ValueAsXrp = 1m }, Reward, false, "1", null);

            Assert.AreEqual(withDestination.Length - 22, without.Length, "Destination is a 22-byte optional field");
            CollectionAssert.DoesNotContain(without.ToList(), (byte)0x83, "no AccountID field 3 header without a destination");
        }

        [TestMethod]
        public void TestUAccountCreateMessage_MatchesRippledLayout()
        {
            byte[] message = XChainAttestationSigner.AccountCreateMessage(
                XrpBridge(), Source, new Currency { ValueAsXrp = 20m }, new Currency { Value = "100", CurrencyCode = "XRP" },
                Destination, Reward, wasLockingChainSend: false, "1");

            List<byte> expected = new List<byte>();
            expected.AddRange(UInt64Field(21, 1));                                   // XChainAccountCreateCount
            expected.AddRange(XrpAmount(0x61, null, 20_000_000));                    // Amount
            expected.AddRange(XrpAmount(0x60, 0x1D, 100));                           // SignatureReward (type 6, field 29)
            expected.AddRange(AccountField(3, Destination));                         // Destination
            expected.AddRange(AccountFieldWide(18, Source));                         // OtherChainSource
            expected.AddRange(AccountFieldWide(21, Reward));                         // AttestationRewardAccount
            expected.AddRange(new byte[] { 0x00, 0x10, 0x13, 0x00 });                // WasLockingChainSend = 0
            expected.AddRange(XrpXrpBridge());                                       // XChainBridge

            Assert.AreEqual(Convert.ToHexString(expected.ToArray()), Convert.ToHexString(message));
        }

        [DataTestMethod]
        [DataRow("ed25519")]
        [DataRow("secp256k1")]
        public void TestUSignClaimAttestation_VerifiesAndDetectsTampering(string algorithm)
        {
            XrplWallet witness = XrplWallet.Generate(algorithm);
            XChainAddClaimAttestation attestation = new XChainAddClaimAttestation
            {
                Account = witness.ClassicAddress,
                XChainBridge = XrpBridge(),
                OtherChainSource = Source,
                Amount = new Currency { ValueAsXrp = 1m },
                AttestationRewardAccount = witness.ClassicAddress,
                Destination = Destination,
                WasLockingChainSend = 1,
                XChainClaimID = "1",
            };

            XChainAttestationSigner.SignClaimAttestation(attestation, witness);

            Assert.AreEqual(witness.PublicKey, attestation.PublicKey);
            Assert.AreEqual(witness.ClassicAddress, attestation.AttestationSignerAccount, "the signer account defaults to the witness");
            Assert.IsTrue(XChainAttestationSigner.VerifyClaimAttestation(attestation));

            attestation.Amount = new Currency { ValueAsXrp = 2m };
            Assert.IsFalse(XChainAttestationSigner.VerifyClaimAttestation(attestation), "a changed amount must not verify");
        }

        [TestMethod]
        public void TestUSignAccountCreateAttestation_Verifies()
        {
            XrplWallet witness = XrplWallet.Generate();
            XChainAddAccountCreateAttestation attestation = new XChainAddAccountCreateAttestation
            {
                Account = witness.ClassicAddress,
                XChainBridge = XrpBridge(),
                XChainAccountCreateCount = "1",
                Amount = new Currency { ValueAsXrp = 20m },
                SignatureReward = new Currency { Value = "100", CurrencyCode = "XRP" },
                OtherChainSource = Source,
                Destination = Destination,
                AttestationRewardAccount = witness.ClassicAddress,
                AttestationSignerAccount = Reward,
                WasLockingChainSend = 0,
            };

            XChainAttestationSigner.SignAccountCreateAttestation(attestation, witness);

            Assert.AreEqual(Reward, attestation.AttestationSignerAccount, "an explicit signer account is kept");
            Assert.IsTrue(XChainAttestationSigner.VerifyAccountCreateAttestation(attestation));

            attestation.WasLockingChainSend = 1;
            Assert.IsFalse(XChainAttestationSigner.VerifyAccountCreateAttestation(attestation), "a flipped direction must not verify");
        }

        [TestMethod]
        public void TestUClaimMessage_MissingField_Throws()
        {
            ValidationException ex = Assert.ThrowsExactly<ValidationException>(() =>
                XChainAttestationSigner.ClaimMessage(XrpBridge(), Source, new Currency { ValueAsXrp = 1m }, "", true, "1", null));
            StringAssert.Contains(ex.Message, "attestationRewardAccount");
        }
    }
}
