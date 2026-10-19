using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

// Mainnet CheckCash E14C0351A673C27F6A1F0795EB7D665339F6B8C7EDB84E812DCA795127F25E1F deletes a
// Check that carries SourceTag. rippled sends SourceTag as a number (UInt32 in definitions.json);
// LOCheck declared it as a string, and the failed read of that one member made the whole transaction
// come back as a bare CheckCashResponse with no hash, date or meta.
namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCheckSourceTag
    {
        private const string Hash = "E14C0351A673C27F6A1F0795EB7D665339F6B8C7EDB84E812DCA795127F25E1F";
        private const string CheckId = "D206CB2E0FFA86AD5CF37D40F96DA43F7A9D3116677C9CDDBF7EFFD2559C0655";
        private const uint SourceTag = 804681468;

        private static readonly string ResponsesDirectory =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Responses");

        private static JsonNode LoadResult(string file) =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(ResponsesDirectory, file)))!["result"];

        private static LOCheck DeletedCheck(Meta meta)
        {
            DeletedNode node = meta.AffectedNodes
                .Select(n => n.DeletedNode)
                .Single(n => n is not null && n.LedgerIndex == CheckId);
            return (LOCheck)node.FinalFields;
        }

        [TestMethod]
        public void TxV1_CheckCashDeletingACheckWithSourceTag_KeepsHashDateAndMeta()
        {
            TransactionResponse tx = LoadResult("tx_check_cash_v1_raw.json")
                .Deserialize<TransactionResponse>(XrplJsonOptions.Default);

            Assert.IsInstanceOfType(tx, typeof(CheckCashResponse));
            Assert.AreEqual(Hash, tx.Hash);
            Assert.IsNotNull(tx.Date);
            Assert.AreEqual("rBi1QrVjwaNofisZAQnovoXdHkS73FG1tJ", tx.Account);
            Assert.IsNotNull(tx.Meta);
            Assert.AreEqual("tesSUCCESS", tx.Meta.TransactionResult);
            Assert.AreEqual(SourceTag, DeletedCheck(tx.Meta).SourceTag);
        }

        [TestMethod]
        public void TxV2_CheckCashDeletingACheckWithSourceTag_KeepsMeta()
        {
            TransactionSummary tx = LoadResult("tx_check_cash_raw.json")
                .Deserialize<TransactionSummary>(XrplJsonOptions.Default);

            Assert.AreEqual(Hash, tx.Hash);
            Assert.IsInstanceOfType(tx.Transaction, typeof(CheckCashResponse));
            Assert.IsNotNull(tx.Meta);
            Assert.AreEqual(SourceTag, DeletedCheck(tx.Meta).SourceTag);
        }

        [TestMethod]
        public void LOCheck_SourceTag_RoundTripsAsANumber()
        {
            string json = $$"""{"LedgerEntryType":"Check","SourceTag":{{SourceTag}},"DestinationTag":7}""";

            LOCheck check = JsonSerializer.Deserialize<LOCheck>(json, XrplJsonOptions.Default);
            JsonNode written = JsonNode.Parse(JsonSerializer.Serialize(check, XrplJsonOptions.Default));

            Assert.AreEqual(SourceTag, check.SourceTag);
            Assert.AreEqual(JsonValueKind.Number, written!["SourceTag"]!.GetValueKind());
            Assert.AreEqual(SourceTag, written["SourceTag"]!.GetValue<uint>());
        }
    }
}
