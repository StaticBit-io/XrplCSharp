using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Request parameters xrpld 3.4.1 reads and result fields it writes that the models had no
    /// member for. The request shapes follow rippled's handlers (LedgerEntry.cpp, AccountTx.cpp,
    /// VaultInfo.cpp, AccountObjects.cpp); the result bodies follow what a 3.4.1 node returned.
    /// </summary>
    [TestClass]
    public class TestURpcFieldGaps
    {
        private const string Account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
        private const string Other = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";
        private const string Hash = "F419888E368FC476EB6443EAF32AC7E6174A38D3F0DEF71DF18E27818BCBA7AE";

        private static string Serialize(object request) => JsonSerializer.Serialize(request, XrplJsonOptions.Default);

        private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, XrplJsonOptions.Default);

        [TestMethod]
        public void TestUAccountObjectsRequestCarriesSponsored()
        {
            string json = Serialize(new AccountObjectsRequest(Account) { Sponsored = false });

            StringAssert.Contains(json, "\"sponsored\":false");
        }

        [TestMethod]
        public void TestUAccountTransactionsRequestCarriesTheDelegateFilter()
        {
            string json = Serialize(new AccountTransactionsRequest(Account)
            {
                Delegate = new AccountTransactionsDelegateFilter { DelegateFilter = "authorizer", CounterParty = Other },
            });

            StringAssert.Contains(json, "\"delegate\":{\"delegate_filter\":\"authorizer\",\"counter_party\":\"" + Other + "\"}");
        }

        [TestMethod]
        public void TestUVaultInfoRequestCarriesOwnerAndSeq()
        {
            string json = Serialize(new VaultInfoRequest { Owner = Account, Seq = 7 });

            StringAssert.Contains(json, "\"owner\":\"" + Account + "\"");
            StringAssert.Contains(json, "\"seq\":7");
            Assert.IsFalse(json.Contains("\"vault_id\""), json);
        }

        [TestMethod]
        public void TestULedgerAndFeatureRequestsCarryTheAdminFlags()
        {
            StringAssert.Contains(Serialize(new LedgerRequest { Full = true, Accounts = true }), "\"full\":true");
            StringAssert.Contains(Serialize(new LedgerRequest { Accounts = true }), "\"accounts\":true");
            StringAssert.Contains(Serialize(new ServerFeaturesRequest { Feature = "Clawback", Vetoed = true }), "\"vetoed\":true");
        }

        [TestMethod]
        public void TestULedgerEntryRequestCarriesTheNewSelectors()
        {
            StringAssert.Contains(
                Serialize(new LedgerEntryRequest { Vault = new VaultQuery { Owner = Account, Seq = 5 } }),
                "\"vault\":{\"owner\":\"" + Account + "\",\"seq\":5}");
            StringAssert.Contains(
                Serialize(new LedgerEntryRequest { Sponsorship = new SponsorshipQuery { Sponsor = Account, Sponsee = Other } }),
                "\"sponsorship\":{\"sponsor\":\"" + Account + "\",\"sponsee\":\"" + Other + "\"}");
            StringAssert.Contains(Serialize(new LedgerEntryRequest { Fee = true }), "\"fee\":true");
            StringAssert.Contains(Serialize(new LedgerEntryRequest { NegativeUnl = true }), "\"nunl\":true");
            StringAssert.Contains(Serialize(new LedgerEntryRequest { Hashes = 1000u }), "\"hashes\":1000");
        }

        [TestMethod]
        public void TestULedgerEntryRequestPutsTheBridgeFieldsBesideTheClaimId()
        {
            XChainBridgeModel bridge = new XChainBridgeModel
            {
                LockingChainDoor = Account,
                LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
                IssuingChainDoor = Other,
                IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
            };

            string json = Serialize(new LedgerEntryRequest
            {
                Bridge = bridge,
                BridgeAccount = Account,
                XChainOwnedClaimId = new XChainOwnedClaimIdQuery
                {
                    LockingChainDoor = Account,
                    LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
                    IssuingChainDoor = Other,
                    IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
                    XChainOwnedClaimId = 3,
                },
            });

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            Assert.AreEqual(Account, root.GetProperty("bridge").GetProperty("LockingChainDoor").GetString());
            Assert.AreEqual(Account, root.GetProperty("bridge_account").GetString());
            JsonElement claim = root.GetProperty("xchain_owned_claim_id");
            Assert.AreEqual(Other, claim.GetProperty("IssuingChainDoor").GetString());
            Assert.AreEqual(3, claim.GetProperty("xchain_owned_claim_id").GetInt32());
        }

        [TestMethod]
        public void TestUAccountChannelsReadsTheLedgerFields()
        {
            AccountChannels closed = Read<AccountChannels>(
                "{\"account\":\"" + Account + "\",\"channels\":[],\"ledger_hash\":\"" + Hash + "\",\"ledger_index\":7,\"validated\":true}");
            AccountChannels open = Read<AccountChannels>(
                "{\"account\":\"" + Account + "\",\"channels\":[],\"ledger_current_index\":8,\"validated\":false}");

            Assert.AreEqual(7u, closed.LedgerIndex);
            Assert.AreEqual(Hash, closed.LedgerHash);
            Assert.AreEqual(true, closed.Validated);
            Assert.AreEqual(8u, open.LedgerCurrentIndex);
            Assert.AreEqual(false, open.Validated);
        }

        [TestMethod]
        public void TestULedgerEntryReadsTheBinaryFormAndTheLedgerFields()
        {
            LedgerEntryResponse entry = Read<LedgerEntryResponse>(
                "{\"index\":\"4BC50C9B0D8515D3EAAE1E74B29A95804346C491EE1A95BF25E4AAB854A6A651\",\"ledger_hash\":\"" + Hash
                + "\",\"ledger_index\":7,\"node_binary\":\"1100732200000000\",\"validated\":true}");

            Assert.AreEqual("1100732200000000", entry.NodeBinary);
            Assert.AreEqual(7u, entry.LedgerIndex);
            Assert.AreEqual(Hash, entry.LedgerHash);
            Assert.AreEqual(true, entry.Validated);
            Assert.IsTrue(entry.UnknownFields == null || entry.UnknownFields.Count == 0, "every field the node sent has a member");
        }

        [TestMethod]
        public void TestUGatewayBalancesReadsLockedAndWritesItBack()
        {
            GatewayBalancesResponse balances = Read<GatewayBalancesResponse>(
                "{\"account\":\"" + Account + "\",\"ledger_current_index\":9,\"locked\":{\"XRP\":\"5000000\",\"USD\":\"12.5\"},\"validated\":false}");

            Assert.AreEqual(9u, balances.LedgerCurrentIndex);
            Assert.AreEqual("5000000", balances.Locked["XRP"]);
            Assert.AreEqual("12.5", balances.Locked["USD"]);

            GatewayBalancesResponse roundTrip = Read<GatewayBalancesResponse>(Serialize(balances));
            Assert.AreEqual("12.5", roundTrip.Locked["USD"]);
            Assert.AreEqual(9u, roundTrip.LedgerCurrentIndex);
        }

        [TestMethod]
        public void TestUSmallerResultsReadTheirMissingFields()
        {
            Assert.AreEqual("201C", Read<SimulateResponse>("{\"applied\":false,\"meta_blob\":\"201C\"}").MetaBlob);
            Assert.AreEqual(50, Read<AccountOffers>("{\"account\":\"" + Account + "\",\"offers\":[],\"limit\":50,\"marker\":\"AB\"}").Limit);
            Assert.AreEqual(true, Read<AccountOffers>("{\"account\":\"" + Account + "\",\"offers\":[],\"validated\":true}").Validated);
            Assert.AreEqual(true, Read<TransactionEntryResponse>("{\"ledger_index\":7,\"validated\":true}").Validated);
            Assert.AreEqual(Hash, Read<AccountInfo>("{\"ledger_hash\":\"" + Hash + "\",\"ledger_index\":7,\"validated\":true}").LedgerHash);
            Assert.AreEqual(7u, Read<AccountNFTs>("{\"account\":\"" + Account + "\",\"account_nfts\":[],\"ledger_index\":7,\"validated\":true}").LedgerIndex);
            Assert.AreEqual(8, Read<AccountCurrencies>("{\"ledger_current_index\":8,\"validated\":false}").LedgerCurrentIndex);
            Assert.AreEqual(8u, Read<VaultInfoResponse>("{\"ledger_current_index\":8,\"validated\":false}").LedgerCurrentIndex);
            Assert.AreEqual(8u, Read<RipplePathFindResponse>("{\"alternatives\":[],\"ledger_current_index\":8,\"validated\":false}").LedgerCurrentIndex);
        }
    }
}
