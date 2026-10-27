using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Binary round trips, JSON reads and validation for the ConfidentialMPTKeyRotation and
    /// Cosign transactions, the TransactionProposal object, and the LoanBrokerSet DomainID rules.
    /// </summary>
    [TestClass]
    public class TestUKeyRotationAndProposal
    {
        private const string IssuanceId = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        private const string Hash256Hex = "1D5A924A24F9AF5F1BF2AD9F4E385E4C0F5B85B94E4C4A1D8B6E1C0B7F3D2E1A";
        private const string ZeroHash256 = "0000000000000000000000000000000000000000000000000000000000000000";
        private const string BlobHex = "DEADBEEF00112233445566778899AABBCCDDEEFF";

        private static string Account1;
        private static string Account2;

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            Account1 = XrplWallet.Generate().ClassicAddress;
            Account2 = XrplWallet.Generate().ClassicAddress;
        }

        private static JsonObject RoundTrip(TransactionRequest tx)
        {
            JsonObject json = JsonNode.Parse(tx.ToJson())!.AsObject();
            json["Sequence"] = 1u;
            json["Fee"] = "12";
            json["SigningPubKey"] = "";
            string blob = XrplBinaryCodec.Encode(json);
            return XrplBinaryCodec.Decode(blob).AsObject();
        }

        private static Dictionary<string, object> AsDictionary(TransactionRequest tx) =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(tx.ToJson(), XrplJsonOptions.Default);

        private static Payment ProposedPayment() => new Payment
        {
            Account = Account1,
            Destination = Account2,
            Amount = new Xrpl.Models.Common.Currency { ValueAsXrp = 1 },
            Sequence = 0,
            TicketSequence = 7,
            Fee = new Xrpl.Models.Common.Currency { Value = "12" },
            SigningPublicKey = "",
        };

        [TestMethod]
        public void TestUConfidentialMPTMirrorUpdate_BinaryRoundTrip()
        {
            ConfidentialMPTMirrorUpdate tx = new ConfidentialMPTMirrorUpdate
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
                Holder = Account2,
                AuditorEncryptedAmount = BlobHex,
                ZKProof = BlobHex,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("ConfidentialMPTMirrorUpdate", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(Account2, decoded["Holder"]!.GetValue<string>());
            Assert.AreEqual(BlobHex, decoded["AuditorEncryptedAmount"]!.GetValue<string>());
            Assert.IsNull(decoded["IssuerEncryptedAmount"]);
        }

        [TestMethod]
        public void TestUValidateConfidentialMPTMirrorUpdate_NeedsACiphertextAndAProof()
        {
            ConfidentialMPTMirrorUpdate tx = new ConfidentialMPTMirrorUpdate
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
                ZKProof = BlobHex,
            };
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            tx.IssuerEncryptedAmount = BlobHex;
            Validation.Validate(AsDictionary(tx));

            tx.ZKProof = null;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
        }

        [TestMethod]
        public void TestUConfidentialMPTHolderKeyUpdate_BinaryRoundTrip()
        {
            ConfidentialMPTHolderKeyUpdate tx = new ConfidentialMPTHolderKeyUpdate
            {
                Account = Account1,
                Flags = ConfidentialMPTHolderKeyUpdateFlags.tfHolderKeyRotation,
                MPTokenIssuanceID = IssuanceId,
                HolderEncryptionKey = BlobHex,
                ConfidentialBalanceSpending = BlobHex,
                ConfidentialBalanceInbox = BlobHex,
                ZKProof = BlobHex,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("ConfidentialMPTHolderKeyUpdate", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(0x00010000u, decoded["Flags"]!.GetValue<uint>());
            Assert.AreEqual(BlobHex, decoded["ConfidentialBalanceInbox"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUValidateConfidentialMPTHolderKeyUpdate_ModeRules()
        {
            ConfidentialMPTHolderKeyUpdate tx = new ConfidentialMPTHolderKeyUpdate
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
            };

            // No mode flag, then two of them.
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
            tx.Flags = ConfidentialMPTHolderKeyUpdateFlags.tfHolderKeyRotation | ConfidentialMPTHolderKeyUpdateFlags.tfCancelRecovery;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            // Cancel carries nothing.
            tx.Flags = ConfidentialMPTHolderKeyUpdateFlags.tfCancelRecovery;
            Validation.Validate(AsDictionary(tx));
            tx.ZKProof = BlobHex;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            // Recovery needs the key and the proof, and no balances.
            tx.Flags = ConfidentialMPTHolderKeyUpdateFlags.tfHolderKeyRecovery;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
            tx.HolderEncryptionKey = BlobHex;
            Validation.Validate(AsDictionary(tx));
            tx.ConfidentialBalanceInbox = BlobHex;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            // Rotation needs both balances.
            tx.Flags = ConfidentialMPTHolderKeyUpdateFlags.tfHolderKeyRotation;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
            tx.ConfidentialBalanceSpending = BlobHex;
            Validation.Validate(AsDictionary(tx));
        }

        [TestMethod]
        public void TestUTransactionProposalCreate_BinaryRoundTrip_KeepsTheProposedTransaction()
        {
            TransactionProposalCreate tx = new TransactionProposalCreate
            {
                Account = Account1,
                ProposedTransaction = ProposedPayment(),
                Expiration = 900000000,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("TransactionProposalCreate", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(900000000u, decoded["Expiration"]!.GetValue<uint>());

            JsonObject proposed = decoded["ProposedTransaction"]!.AsObject();
            Assert.AreEqual("Payment", proposed["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(Account2, proposed["Destination"]!.GetValue<string>());
            Assert.AreEqual(0u, proposed["Sequence"]!.GetValue<uint>());
            Assert.AreEqual(7u, proposed["TicketSequence"]!.GetValue<uint>());
            Assert.AreEqual("", proposed["SigningPubKey"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUValidateTransactionProposalCreate_StatelessRules()
        {
            TransactionProposalCreate tx = new TransactionProposalCreate
            {
                Account = Account1,
                ProposedTransaction = ProposedPayment(),
                Expiration = 900000000,
            };
            Validation.Validate(AsDictionary(tx));

            tx.Expiration = 0;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
            tx.Expiration = 900000000;

            Payment sequenced = ProposedPayment();
            sequenced.Sequence = 5;
            tx.ProposedTransaction = sequenced;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            Payment signed = ProposedPayment();
            signed.TransactionSignature = BlobHex;
            tx.ProposedTransaction = signed;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            Payment keyed = ProposedPayment();
            keyed.SigningPublicKey = BlobHex;
            tx.ProposedTransaction = keyed;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            tx.ProposedTransaction = null;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
        }

        [TestMethod]
        public void TestUTransactionProposalCancel_RoundTripAndZeroId()
        {
            TransactionProposalCancel tx = new TransactionProposalCancel
            {
                Account = Account1,
                ProposalID = Hash256Hex,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("TransactionProposalCancel", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(Hash256Hex, decoded["ProposalID"]!.GetValue<string>());
            Validation.Validate(AsDictionary(tx));

            tx.ProposalID = ZeroHash256;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
        }

        [TestMethod]
        public void TestUTransactionProposalCreateResponse_ReadsTheProposedTransactionAsItsType()
        {
            string json = $@"{{
                ""TransactionType"": ""TransactionProposalCreate"",
                ""Account"": ""{Account1}"",
                ""Expiration"": 900000000,
                ""ProposedTransaction"": {{
                    ""TransactionType"": ""Payment"",
                    ""Account"": ""{Account1}"",
                    ""Destination"": ""{Account2}"",
                    ""Amount"": ""1000000"",
                    ""Sequence"": 0,
                    ""TicketSequence"": 7,
                    ""SigningPubKey"": """"
                }}
            }}";
            ITransactionResponse response = JsonSerializer.Deserialize<ITransactionResponse>(json, XrplJsonOptions.Default);

            TransactionProposalCreateResponse create = (TransactionProposalCreateResponse)response;
            Assert.AreEqual(900000000u, create.Expiration);
            Payment proposed = (Payment)create.ProposedTransaction;
            Assert.AreEqual(Account2, proposed.Destination);
            Assert.AreEqual(7u, proposed.TicketSequence);
        }

        [TestMethod]
        public void TestULOTransactionProposal_Read()
        {
            string json = $@"{{
                ""LedgerEntryType"": ""TransactionProposal"",
                ""Flags"": 0,
                ""Owner"": ""{Account1}"",
                ""Expiration"": 900000000,
                ""OwnerNode"": ""0"",
                ""PreviousTxnID"": ""{Hash256Hex}"",
                ""PreviousTxnLgrSeq"": 12,
                ""ProposedTransaction"": {{
                    ""TransactionType"": ""Payment"",
                    ""Account"": ""{Account1}"",
                    ""Destination"": ""{Account2}"",
                    ""Amount"": ""1000000"",
                    ""Sequence"": 0,
                    ""TicketSequence"": 7,
                    ""SigningPubKey"": """"
                }}
            }}";
            BaseLedgerEntry entry = JsonSerializer.Deserialize<BaseLedgerEntry>(json, XrplJsonOptions.Default);

            LOTransactionProposal proposal = (LOTransactionProposal)entry;
            Assert.AreEqual(LedgerEntryType.TransactionProposal, proposal.LedgerEntryType);
            Assert.AreEqual(Account1, proposal.Owner);
            Assert.AreEqual(900000000u, proposal.Expiration);
            Assert.IsInstanceOfType(proposal.ProposedTransaction, typeof(Payment));
        }

        [TestMethod]
        public void TestUValidateLoanBrokerSet_DomainIdRules()
        {
            LoanBrokerSet tx = new LoanBrokerSet
            {
                Account = Account1,
                VaultID = Hash256Hex,
                DomainID = Hash256Hex,
            };

            // A new public broker cannot have a domain.
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));

            tx.Flags = LoanBrokerSetFlags.tfLoanBrokerPrivate;
            Validation.Validate(AsDictionary(tx));

            // Zero is refused on create, and is how an existing broker drops its domain.
            tx.DomainID = ZeroHash256;
            Assert.ThrowsExactly<ValidationException>(() => Validation.Validate(AsDictionary(tx)));
            tx.LoanBrokerID = Hash256Hex;
            Validation.Validate(AsDictionary(tx));
        }

        [TestMethod]
        public void TestULoanBrokerSet_DomainIdAndFlagRoundTrip()
        {
            LoanBrokerSet tx = new LoanBrokerSet
            {
                Account = Account1,
                VaultID = Hash256Hex,
                DomainID = Hash256Hex,
                Flags = LoanBrokerSetFlags.tfLoanBrokerPrivate,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual(Hash256Hex, decoded["DomainID"]!.GetValue<string>());
            Assert.AreEqual(0x00010000u, decoded["Flags"]!.GetValue<uint>());
        }

        [TestMethod]
        public void TestUSmartEscrowFields_BinaryRoundTrip()
        {
            EscrowCreate create = new EscrowCreate
            {
                Account = Account1,
                Destination = Account2,
                Amount = new Xrpl.Models.Common.Currency { Value = "1000000" },
                FinishAfter = new System.DateTime(2030, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                Bytecode = BlobHex,
                Data = "00",
            };
            JsonObject decodedCreate = RoundTrip(create);
            Assert.AreEqual(BlobHex, decodedCreate["Bytecode"]!.GetValue<string>());
            Assert.AreEqual("00", decodedCreate["Data"]!.GetValue<string>());

            EscrowFinish finish = new EscrowFinish
            {
                Account = Account2,
                Owner = Account1,
                OfferSequence = 3,
                Gas = 1000,
            };
            Assert.AreEqual(1000u, RoundTrip(finish)["Gas"]!.GetValue<uint>());
        }
    }
}
