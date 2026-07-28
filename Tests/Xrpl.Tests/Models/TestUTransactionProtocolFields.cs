using System.Collections.Generic;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using TxFormat = Xrpl.Models.Transaction.TxFormat;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Pins the transaction fields that the protocol declares but the models used to drop:
    /// the common fields sfOperationLimit and sfDelegate, and the AccountSet-local
    /// sfWalletLocator / sfWalletSize. Also pins that unset fields stay out of the
    /// signed bytes, and that the retired sfTarget is not resurrected.
    /// </summary>
    [TestClass]
    public class TestUTransactionProtocolFields
    {
        // Deterministic Ed25519 key material - the same seed yields the same signature bytes.
        private const string Seed = "sEdSKaCy2JT7JaM7v95H9SxkhP9wS2r";
        private const string DestinationSeed = "sEdTM1uX8pu2do5XvTnutH6HsouMaM2";

        // Blobs captured from the released 10.9.1 signing path BEFORE the new properties existed.
        // Any change to the bytes of a transaction that does not set the new fields breaks these.
        private const string GoldenAccountSet =
            "120003240000000168400000000000000C7321ED01FA53FA5A7E77798F882ECE20B1ABC00BB358A9E55A202D0D0676BD0CE37A63" +
            "74405E9B4DB6638729743A12BCF8A1E67B2E1AF0DBAE7DE2DCCF6422E2D00E2D7EB9F615B1A6FF276E66D3641ECEE3D31A2F0E22" +
            "1FEE92654280BFF5C160670C300E8114D28B177E48D9A8D057E70F7E464B498367281B98";

        private const string GoldenPayment =
            "12000024000000016140000000000F424068400000000000000C7321ED01FA53FA5A7E77798F882ECE20B1ABC00BB358A9E55A20" +
            "2D0D0676BD0CE37A6374405774060AC195CEF17762B1989FC4FD0E0F2F1D9705A1397EC6B368615110F353E1BC1DA7F679A29F0F" +
            "C5745BF9FF2567531CCE74D645E470CD7FDB5A0A5820058114D28B177E48D9A8D057E70F7E464B498367281B988314A6070B8A18" +
            "22E3322676A99F0C804EE2D15B8270";

        private const string GoldenTrustSet =
            "1200142200020000240000000163D5038D7EA4C680000000000000000000000000005553440000000000A6070B8A1822E3322676" +
            "A99F0C804EE2D15B827068400000000000000C7321ED01FA53FA5A7E77798F882ECE20B1ABC00BB358A9E55A202D0D0676BD0CE3" +
            "7A637440F2ACDE17AEFF830E0366478A0C48BFAB6AC7B6BC40D9D95F430A56533541484B909075B79C62BE5B610258494F402599" +
            "DA18C390B9F868399BBD11C4BA27D0038114D28B177E48D9A8D057E70F7E464B498367281B98";

        private static XrplWallet Wallet => XrplWallet.FromSeed(Seed);

        private static string Destination => XrplWallet.FromSeed(DestinationSeed).ClassicAddress;

        private static Currency MinimalFee => new Currency { Value = "12" };

        [TestMethod]
        public void TestUOperationLimit_SurvivesResponseDeserialization()
        {
            // Xahau Burn-2-Mint marks the burn with OperationLimit = destination network id.
            // Reading such a transaction back used to lose the marker entirely.
            string json = $@"{{
                ""TransactionType"": ""AccountSet"",
                ""Account"": ""{Wallet.ClassicAddress}"",
                ""Fee"": ""5000000"",
                ""Sequence"": 1,
                ""OperationLimit"": 21337
            }}";

            ITransactionResponse response = JsonSerializer.Deserialize<ITransactionResponse>(json, XrplJsonOptions.Default);

            Assert.IsInstanceOfType<AccountSetResponse>(response);
            Assert.AreEqual(21337u, ((TransactionResponse)response).OperationLimit);
            StringAssert.Contains(response.ToJson(), "\"OperationLimit\":21337");
            Assert.IsTrue(Xrpl.Models.Transactions.Common.TryGetUInt32(response.ToDictionary()["OperationLimit"], out uint fromDictionary));
            Assert.AreEqual(21337u, fromDictionary);
        }

        [TestMethod]
        public void TestUOperationLimit_TypedSigningMatchesDictionaryPath()
        {
            // The dictionary route is the workaround consumers use today; the typed model must
            // produce byte-identical output so switching to it cannot change a signature.
            AccountSet typed = new AccountSet(Wallet.ClassicAddress)
            {
                Fee = MinimalFee,
                Sequence = 1,
                OperationLimit = 21337,
                SigningPublicKey = Wallet.PublicKey,
            };

            Dictionary<string, object> viaDictionary = new Dictionary<string, object>
            {
                ["TransactionType"] = "AccountSet",
                ["Account"] = Wallet.ClassicAddress,
                ["Fee"] = "12",
                ["Sequence"] = 1u,
                ["OperationLimit"] = 21337u,
                ["SigningPubKey"] = Wallet.PublicKey,
            };

            Assert.AreEqual(Wallet.Sign(viaDictionary).TxBlob, Wallet.Sign(typed).TxBlob);
            Assert.AreEqual(21337u, XrplBinaryCodec.Decode(Wallet.Sign(typed).TxBlob).AsObject()["OperationLimit"].GetValue<uint>());
        }

        [TestMethod]
        public void TestUDelegate_SurvivesResponseDeserialization()
        {
            string json = $@"{{
                ""TransactionType"": ""Payment"",
                ""Account"": ""{Wallet.ClassicAddress}"",
                ""Destination"": ""{Destination}"",
                ""Amount"": ""1000000"",
                ""Fee"": ""12"",
                ""Sequence"": 1,
                ""Delegate"": ""{Destination}""
            }}";

            ITransactionResponse response = JsonSerializer.Deserialize<ITransactionResponse>(json, XrplJsonOptions.Default);

            Assert.IsInstanceOfType<PaymentResponse>(response);
            Assert.AreEqual(Destination, ((TransactionResponse)response).Delegate);
            StringAssert.Contains(response.ToJson(), "\"Delegate\":");
        }

        [TestMethod]
        public void TestUDelegate_TypedSigningMatchesDictionaryPath()
        {
            Payment typed = new Payment
            {
                Account = Wallet.ClassicAddress,
                Destination = Destination,
                Amount = new Currency { ValueAsXrp = 1m },
                Fee = MinimalFee,
                Sequence = 1,
                Delegate = Destination,
                SigningPublicKey = Wallet.PublicKey,
            };

            Dictionary<string, object> viaDictionary = new Dictionary<string, object>
            {
                ["TransactionType"] = "Payment",
                ["Account"] = Wallet.ClassicAddress,
                ["Destination"] = Destination,
                ["Amount"] = "1000000",
                ["Fee"] = "12",
                ["Sequence"] = 1u,
                ["Delegate"] = Destination,
                ["SigningPubKey"] = Wallet.PublicKey,
            };

            Assert.AreEqual(Wallet.Sign(viaDictionary).TxBlob, Wallet.Sign(typed).TxBlob);
            Assert.AreEqual(Destination, XrplBinaryCodec.Decode(Wallet.Sign(typed).TxBlob).AsObject()["Delegate"].GetValue<string>());
        }

        [TestMethod]
        public void TestUAccountSetWalletFields_RoundTripThroughBinaryCodec()
        {
            AccountSet typed = new AccountSet(Wallet.ClassicAddress)
            {
                Fee = MinimalFee,
                Sequence = 1,
                WalletLocator = new string('A', 64),
                WalletSize = 3,
                SigningPublicKey = Wallet.PublicKey,
            };

            System.Text.Json.Nodes.JsonObject decoded = XrplBinaryCodec.Decode(Wallet.Sign(typed).TxBlob).AsObject();

            Assert.AreEqual(new string('A', 64), decoded["WalletLocator"].GetValue<string>());
            Assert.AreEqual(3u, decoded["WalletSize"].GetValue<uint>());
        }

        [TestMethod]
        public void TestUAccountSetWalletFields_SurviveResponseDeserialization()
        {
            string json = $@"{{
                ""TransactionType"": ""AccountSet"",
                ""Account"": ""{Wallet.ClassicAddress}"",
                ""Fee"": ""12"",
                ""Sequence"": 1,
                ""WalletLocator"": ""{new string('A', 64)}"",
                ""WalletSize"": 3
            }}";

            AccountSetResponse response = (AccountSetResponse)JsonSerializer.Deserialize<ITransactionResponse>(json, XrplJsonOptions.Default);

            Assert.AreEqual(new string('A', 64), response.WalletLocator);
            Assert.AreEqual(3u, response.WalletSize);
        }

        [TestMethod]
        public void TestUUnsetProtocolFields_LeaveSignedBytesUnchanged()
        {
            // The regression guard for adding fields to the common base: a transaction that
            // does not set them must sign to exactly the bytes it signed to before.
            AccountSet accountSet = new AccountSet(Wallet.ClassicAddress)
            {
                Fee = MinimalFee,
                Sequence = 1,
                SigningPublicKey = Wallet.PublicKey,
            };
            Payment payment = new Payment
            {
                Account = Wallet.ClassicAddress,
                Destination = Destination,
                Amount = new Currency { ValueAsXrp = 1m },
                Fee = MinimalFee,
                Sequence = 1,
                SigningPublicKey = Wallet.PublicKey,
            };
            TrustSet trustSet = new TrustSet
            {
                Account = Wallet.ClassicAddress,
                LimitAmount = new Currency { CurrencyCode = "USD", Issuer = Destination, Value = "100" },
                Fee = MinimalFee,
                Sequence = 1,
                SigningPublicKey = Wallet.PublicKey,
            };

            Assert.AreEqual(GoldenAccountSet, Wallet.Sign(accountSet).TxBlob);
            Assert.AreEqual(GoldenPayment, Wallet.Sign(payment).TxBlob);
            Assert.AreEqual(GoldenTrustSet, Wallet.Sign(trustSet).TxBlob);
        }

        [TestMethod]
        public void TestUUnsetProtocolFields_StayOutOfOutgoingJson()
        {
            string json = new Payment
            {
                Account = Wallet.ClassicAddress,
                Destination = Destination,
                Amount = new Currency { ValueAsXrp = 1m },
                Fee = MinimalFee,
                Sequence = 1,
                SigningPublicKey = Wallet.PublicKey,
            }.ToJson();

            foreach (string field in new[] { "OperationLimit", "Delegate", "WalletLocator", "WalletSize" })
            {
                Assert.IsFalse(json.Contains(field), $"{field} must not appear in outgoing JSON when unset");
            }
        }

        [TestMethod]
        public async System.Threading.Tasks.Task TestUBaseTransaction_ValidatesNewCommonFieldTypes()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                ["TransactionType"] = "AccountSet",
                ["Account"] = Wallet.ClassicAddress,
            };

            tx["OperationLimit"] = 21337u;
            tx["Delegate"] = Destination;
            await Xrpl.Models.Transactions.Common.ValidateBaseTransaction(tx);

            tx["OperationLimit"] = "not a number";
            await Assert.ThrowsExactlyAsync<Xrpl.Client.Exceptions.ValidationException>(
                () => Xrpl.Models.Transactions.Common.ValidateBaseTransaction(tx));

            tx["OperationLimit"] = 21337u;
            tx["Delegate"] = 12345;
            await Assert.ThrowsExactlyAsync<Xrpl.Client.Exceptions.ValidationException>(
                () => Xrpl.Models.Transactions.Common.ValidateBaseTransaction(tx));
        }

        [TestMethod]
        public void TestUCommonFields_AreReachableThroughTheInterface()
        {
            // Delegate/OperationLimit (rippled commonFields) and Sponsor/SponsorFlags (XLS-68)
            // are common to every transaction, so ITransactionCommon must expose them:
            // otherwise anything typed by the interface — Batch.RawTransaction,
            // SimulateRequest.Transaction — can only reach them through a cast.
            ITransactionCommon request = new AccountSet(Wallet.ClassicAddress)
            {
                Delegate = Destination,
                OperationLimit = 21337,
                Sponsor = Destination,
                SponsorFlags = SponsorCoverage.spfSponsorFee,
            };

            Assert.AreEqual(Destination, request.Delegate);
            Assert.AreEqual(21337u, request.OperationLimit);
            Assert.AreEqual(Destination, request.Sponsor);
            Assert.AreEqual(SponsorCoverage.spfSponsorFee, request.SponsorFlags);

            ITransactionCommon response = new AccountSetResponse
            {
                Account = Wallet.ClassicAddress,
                Delegate = Destination,
                OperationLimit = 21337,
                Sponsor = Destination,
                SponsorFlags = SponsorCoverage.spfSponsorFee,
            };

            Assert.AreEqual(Destination, response.Delegate);
            Assert.AreEqual(21337u, response.OperationLimit);
            Assert.AreEqual(Destination, response.Sponsor);
            Assert.AreEqual(SponsorCoverage.spfSponsorFee, response.SponsorFlags);
        }

        [TestMethod]
        public void TestUBatchInnerTransaction_ExposesDelegateWithoutACast()
        {
            // The concrete reason this matters: BatchUtils resolves required signers from an
            // inner transaction's Delegate, but Batch.RawTransaction is typed ITransactionRequest.
            IBatch batch = new Batch
            {
                Account = Wallet.ClassicAddress,
                RawTransactions = new List<RawTransactionWrapper>
                {
                    new RawTransactionWrapper
                    {
                        RawTransaction = new Payment
                        {
                            Account = Wallet.ClassicAddress,
                            Destination = Destination,
                            Amount = new Currency { ValueAsXrp = 1m },
                            Delegate = Destination,
                        },
                    },
                },
            };

            ITransactionRequest inner = batch.RawTransactions[0].RawTransaction;
            Assert.AreEqual(Destination, inner.Delegate);
        }

        [TestMethod]
        public void TestUTicketCreate_DoesNotCarryRetiredTargetField()
        {
            // sfTarget is retired in rippled (AccountID nth 7 is marked unused) and absent from
            // definitions.json; TicketCreate carries only sfTicketCount since the TicketBatch amendment.
            TxFormat ticketCreate = TxFormat.Formats[BinaryCodec.Types.TransactionType.TicketCreate];

            Assert.IsTrue(ticketCreate.ContainsKey(BinaryCodec.Enums.Field.TicketCount));
            Assert.IsFalse(ticketCreate.ContainsKey(BinaryCodec.Enums.Field.Target));
            Assert.IsFalse(ticketCreate.ContainsKey(BinaryCodec.Enums.Field.Expiration));
        }
    }
}
