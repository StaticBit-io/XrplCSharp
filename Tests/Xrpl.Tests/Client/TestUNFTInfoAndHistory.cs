using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// The two Clio commands that answer "who owns this token" and "what happened to it" - issue #132.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neither had a model, and neither has a substitute. Ownership cannot be read out of
    /// <c>nft_sell_offers</c>, which is the natural guess: a sale does not remove offers for the
    /// token from the ledger, so offers made by a previous owner keep being returned after they can
    /// no longer be accepted, and the new owner has usually made none - which is exactly the state
    /// a token is in immediately after being bought.
    /// </para>
    /// <para>
    /// The field names here were taken from Clio's own handlers rather than from documentation:
    /// <c>NFTInfo.cpp</c> and <c>NFTHistory.cpp</c>. That matters for at least one of them -
    /// Clio emits <c>nft_serial</c> while its own source notes the documentation calls it
    /// <c>nft_sequence</c>.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUNFTInfoAndHistory
    {
        private const string TokenId = "00190000E78F76A49DD9158FA85DA4AAD95C0767303CC4611D73BB4300C989A8";

        [TestMethod]
        public void TestUNFTInfoRequestAsksForWhatClioExpects()
        {
            NFTInfoRequest request = new NFTInfoRequest(TokenId)
            {
                LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
            };

            string json = JsonSerializer.Serialize(request, XrplJsonOptions.Default);

            StringAssert.Contains(json, "\"command\":\"nft_info\"");
            StringAssert.Contains(json, "\"nft_id\":\"" + TokenId + "\"");
            StringAssert.Contains(json, "\"ledger_index\":\"validated\"");
        }

        /// <summary>
        /// The answer, read from a body shaped the way Clio writes it.
        /// </summary>
        [TestMethod]
        public void TestUNFTInfoReadsEveryFieldClioSends()
        {
            const string body = """
                {
                  "nft_id": "00190000E78F76A49DD9158FA85DA4AAD95C0767303CC4611D73BB4300C989A8",
                  "ledger_index": 270,
                  "owner": "rG9gdNhhCXhK1UVLbaBHzXHZzYrDVJHbAM",
                  "is_burned": false,
                  "flags": 25,
                  "transfer_fee": 314,
                  "issuer": "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                  "nft_taxon": 0,
                  "nft_serial": 12345,
                  "uri": "697066733A2F2F62616679626569676479727A74357366703775646D3768753736",
                  "validated": true
                }
                """;

            NFTInfo info = JsonSerializer.Deserialize<NFTInfo>(body, XrplJsonOptions.Default);

            Assert.AreEqual(TokenId, info.NFTokenID);
            Assert.AreEqual(270u, info.LedgerIndex);
            Assert.AreEqual("rG9gdNhhCXhK1UVLbaBHzXHZzYrDVJHbAM", info.Owner);
            Assert.IsFalse(info.IsBurned.Value, "This one is alive; a burned token has no owner to report.");
            Assert.AreEqual(25u, info.Flags);
            Assert.AreEqual(314u, info.TransferFee);
            Assert.AreEqual("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", info.Issuer);
            Assert.AreEqual(0u, info.Taxon);
            Assert.AreEqual(12345u, info.Serial, "Clio sends this as nft_serial, whatever the documentation calls it.");
            Assert.IsFalse(string.IsNullOrEmpty(info.URI));
            Assert.IsTrue(info.Validated.Value);

            // The other half of the claim, and the half the assertions above cannot make: that
            // nothing Clio sends was missed. Reading eleven properties correctly says nothing about
            // a twelfth quietly landing in UnknownFields - which is the bar this repository already
            // set for modelled fields, a property declared AND the field gone from here.
            Assert.IsTrue(
                info.UnknownFields is null || info.UnknownFields.Count == 0,
                $"nft_info fields the model does not declare: {Describe(info.UnknownFields)}");
        }

        private static string Describe(System.Collections.Generic.IDictionary<string, System.Text.Json.JsonElement> unknown) =>
            unknown is null ? "none" : string.Join(", ", unknown.Keys);

        [TestMethod]
        public void TestUNFTHistoryRequestCarriesItsPagination()
        {
            NFTHistoryRequest request = new NFTHistoryRequest(TokenId)
            {
                LedgerIndexMin = -1,
                LedgerIndexMax = -1,
                Limit = 200,
                Forward = true,
                Marker = new { ledger = 270, seq = 1 },
            };

            string json = JsonSerializer.Serialize(request, XrplJsonOptions.Default);

            StringAssert.Contains(json, "\"command\":\"nft_history\"");
            StringAssert.Contains(json, "\"nft_id\":\"" + TokenId + "\"");
            StringAssert.Contains(json, "\"ledger_index_min\":-1");
            StringAssert.Contains(json, "\"ledger_index_max\":-1");
            StringAssert.Contains(json, "\"limit\":200");
            StringAssert.Contains(json, "\"forward\":true");
            StringAssert.Contains(json, "\"marker\"");
        }

        /// <summary>
        /// History entries are the same shape <c>account_tx</c> returns, so the same type reads them.
        /// </summary>
        /// <remarks>
        /// Asserted rather than assumed, because it is the reason no parallel entry type was
        /// written: <see cref="TransactionSummary"/> already handles the <c>tx</c> and <c>tx_json</c>
        /// envelopes of API v1 and v2, and a second type would be a second place to keep in step
        /// with rippled's envelopes.
        /// </remarks>
        [TestMethod]
        public void TestUNFTHistoryReadsItsTransactionsAndMarker()
        {
            const string body = """
                {
                  "nft_id": "00190000E78F76A49DD9158FA85DA4AAD95C0767303CC4611D73BB4300C989A8",
                  "ledger_index_min": 3,
                  "ledger_index_max": 270,
                  "limit": 2,
                  "marker": { "ledger": 265, "seq": 1 },
                  "transactions": [
                    {
                      "meta": { "TransactionResult": "tesSUCCESS" },
                      "tx_json": {
                        "Account": "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                        "TransactionType": "NFTokenMint",
                        "NFTokenTaxon": 0
                      },
                      "hash": "5F8A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8",
                      "ledger_index": 270,
                      "validated": true
                    }
                  ],
                  "validated": true
                }
                """;

            NFTHistory history = JsonSerializer.Deserialize<NFTHistory>(body, XrplJsonOptions.Default);

            Assert.AreEqual(TokenId, history.NFTokenID);
            Assert.AreEqual(3u, history.LedgerIndexMin);
            Assert.AreEqual(270u, history.LedgerIndexMax);
            Assert.IsNotNull(history.Marker, "A marker means there is more to read, and it must survive to be handed back.");
            Assert.IsNotNull(history.Transactions);
            Assert.AreEqual(1, history.Transactions.Count);

            TransactionSummary entry = history.Transactions[0];
            Assert.AreEqual("tesSUCCESS", entry.Meta?.TransactionResult);
            Assert.IsNotNull(entry.Transaction, "The tx_json envelope must have been read, same as in account_tx.");
            Assert.IsInstanceOfType<INFTokenMint>(
                entry.Transaction,
                "History is read through the I-interfaces; the request type never matches what a ledger sends.");

            Assert.IsTrue(
                history.UnknownFields is null || history.UnknownFields.Count == 0,
                $"nft_history fields the model does not declare: {Describe(history.UnknownFields)}");
        }

        /// <summary>
        /// An answer without a marker is the last page, and that has to be visible.
        /// </summary>
        [TestMethod]
        public void TestUNFTHistoryWithoutAMarkerIsTheLastPage()
        {
            const string body = """
                {
                  "nft_id": "00190000E78F76A49DD9158FA85DA4AAD95C0767303CC4611D73BB4300C989A8",
                  "ledger_index_min": 3,
                  "ledger_index_max": 270,
                  "transactions": [],
                  "validated": true
                }
                """;

            NFTHistory history = JsonSerializer.Deserialize<NFTHistory>(body, XrplJsonOptions.Default);

            Assert.IsNull(history.Marker, "Without this a caller cannot tell the last page from a page that happens to be empty.");
            Assert.IsNotNull(history.Transactions);
            Assert.AreEqual(0, history.Transactions.Count);
        }
    }
}
