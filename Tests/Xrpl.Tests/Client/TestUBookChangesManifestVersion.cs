using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// The <c>book_changes</c>, <c>manifest</c> and <c>version</c> methods, against rippled's
    /// <c>BookChanges.h</c>, <c>Manifest.cpp</c> and <c>ApiVersion.h</c> (<c>setVersion</c>).
    /// </summary>
    [TestClass]
    public class TestUBookChangesManifestVersion
    {
        private const string IssuanceId = "0000012FFD9EE5DA93AC614B4DB94D7E0FCE415CA51BED47";
        private const string DomainId = "F10D0CC9A0F9A3CBF585B80BE09A186483668FDBDD39AA7E3370F3649CE134E5";

        private static string Serialize<T>(T request) => JsonSerializer.Serialize(request, XrplJsonOptions.Default);

        private static T Read<T>(string body) => JsonSerializer.Deserialize<T>(body, XrplJsonOptions.Default);

        [TestMethod]
        public void TestUBookChangesRequestAsksForOneLedger()
        {
            string json = Serialize(new BookChangesRequest { LedgerIndex = new LedgerIndex(88530953) });

            StringAssert.Contains(json, "\"command\":\"book_changes\"");
            StringAssert.Contains(json, "\"ledger_index\":88530953");
        }

        /// <summary>
        /// The answer as xrpl.org documents it, plus the MPT and permissioned-domain members rippled's
        /// <c>computeBookChanges</c> writes: an MPT side has <c>mpt_issuance_id_*</c> instead of <c>currency_*</c>.
        /// </summary>
        [TestMethod]
        public void TestUBookChangesResponseReadsEveryFieldTheNodeSends()
        {
            const string body = $$"""
                {
                  "type": "bookChanges",
                  "ledger_hash": "7AB08A2415C10E07201521F3260F77ADFF4902A528EA66378E259A07767A24B9",
                  "ledger_index": 88530953,
                  "ledger_time": 771100891,
                  "validated": true,
                  "changes": [
                    {
                      "currency_a": "XRP_drops",
                      "currency_b": "rKiCet8SdvWxPXnAgYarFUXMh1zCPz432Y/CNY",
                      "volume_a": "44082741",
                      "volume_b": "158.6978676",
                      "high": "277777.7777777778",
                      "low": "277777.7777777778",
                      "open": "277777.7777777778",
                      "close": "277777.7777777778"
                    },
                    {
                      "currency_a": "XRP_drops",
                      "mpt_issuance_id_b": "{{IssuanceId}}",
                      "volume_a": "1000",
                      "volume_b": "10",
                      "high": "100",
                      "low": "100",
                      "open": "100",
                      "close": "100",
                      "domain": "{{DomainId}}"
                    }
                  ]
                }
                """;

            BookChangesResponse response = Read<BookChangesResponse>(body);

            Assert.AreEqual("bookChanges", response.Type);
            Assert.AreEqual(88530953u, response.LedgerIndex);
            Assert.AreEqual("7AB08A2415C10E07201521F3260F77ADFF4902A528EA66378E259A07767A24B9", response.LedgerHash);
            Assert.AreEqual(new DateTime(2024, 6, 7, 18, 41, 31, DateTimeKind.Utc), response.LedgerTime);
            Assert.IsTrue(response.Validated.Value);
            Assert.HasCount(2, response.Changes);

            BookChange iou = response.Changes[0];
            Assert.IsTrue(iou.IsXrpA);
            Assert.AreEqual("rKiCet8SdvWxPXnAgYarFUXMh1zCPz432Y", iou.AssetB.Issuer);
            Assert.AreEqual("CNY", iou.AssetB.Currency);
            Assert.AreEqual("44082741", iou.VolumeA);
            Assert.AreEqual("158.6978676", iou.VolumeB);
            Assert.AreEqual("277777.7777777778", iou.Close);
            Assert.IsNull(iou.Domain);

            BookChange mpt = response.Changes[1];
            Assert.IsNull(mpt.CurrencyB);
            Assert.AreEqual(IssuanceId, mpt.MptIssuanceIdB);
            Assert.IsTrue(mpt.AssetB.IsMpt());
            Assert.AreEqual(IssuanceId, mpt.AssetB.MptIssuanceId);
            Assert.AreEqual(DomainId, mpt.Domain);
        }

        /// <summary>
        /// The stream and the method carry the same change entries, so the stream reads the MPT and domain members too.
        /// </summary>
        [TestMethod]
        public void TestUBookChangesStreamReadsMptSides()
        {
            const string body = $$"""
                {
                  "type": "bookChanges",
                  "ledger_index": 10,
                  "ledger_hash": "7AB08A2415C10E07201521F3260F77ADFF4902A528EA66378E259A07767A24B9",
                  "ledger_time": 771100891,
                  "changes": [
                    { "mpt_issuance_id_a": "{{IssuanceId}}", "currency_b": "XRP_drops", "volume_a": "1", "volume_b": "2", "high": "0.5", "low": "0.5", "open": "0.5", "close": "0.5" }
                  ]
                }
                """;

            BookChangesStream stream = Read<BookChangesStream>(body);

            Assert.AreEqual(IssuanceId, stream.Changes[0].AssetA.MptIssuanceId);
            Assert.IsTrue(stream.Changes[0].IsXrpB);
        }

        [TestMethod]
        public void TestUManifestRequestAsksForTheKey()
        {
            string json = Serialize(new ManifestRequest("nHUFE9prPXPrHcG3SkwP1UzAQbSphqyQkQK9ATXLZsfkezhhda3p"));

            StringAssert.Contains(json, "\"command\":\"manifest\"");
            StringAssert.Contains(json, "\"public_key\":\"nHUFE9prPXPrHcG3SkwP1UzAQbSphqyQkQK9ATXLZsfkezhhda3p\"");
        }

        [TestMethod]
        public void TestUManifestResponseReadsTheDetails()
        {
            const string body = """
                {
                  "details": {
                    "domain": "example.com",
                    "ephemeral_key": "n9J67zk4B7GpbQV5jRQntbhKNBnkBRhbtqLWNKFgpkbDJjQrGMLU",
                    "master_key": "nHUFE9prPXPrHcG3SkwP1UzAQbSphqyQkQK9ATXLZsfkezhhda3p",
                    "seq": 1
                  },
                  "manifest": "JAAAAAFxIe3AkJgOyqs3y+UuiZiJ6SYYFRTX0xn5lmXbRf4CUsT7mHMhAoYYI7mN+N+ccSmvwyzOJOlVZy2d4hDHA7l7v4fXxDyCdkcwRQIhAMcmgw==",
                  "requested": "nHUFE9prPXPrHcG3SkwP1UzAQbSphqyQkQK9ATXLZsfkezhhda3p"
                }
                """;

            ManifestResponse response = Read<ManifestResponse>(body);

            Assert.AreEqual("nHUFE9prPXPrHcG3SkwP1UzAQbSphqyQkQK9ATXLZsfkezhhda3p", response.Requested);
            StringAssert.StartsWith(response.Manifest, "JAAAAAFxIe3A");
            Assert.AreEqual("nHUFE9prPXPrHcG3SkwP1UzAQbSphqyQkQK9ATXLZsfkezhhda3p", response.Details.MasterKey);
            Assert.AreEqual("n9J67zk4B7GpbQV5jRQntbhKNBnkBRhbtqLWNKFgpkbDJjQrGMLU", response.Details.EphemeralKey);
            Assert.AreEqual(1u, response.Details.Seq);
            Assert.AreEqual("example.com", response.Details.Domain);
        }

        /// <summary>
        /// A key the node has no manifest for is not an error: rippled's manifest cache maps an unknown key
        /// to itself, so the details name it as both keys, with no manifest and no sequence.
        /// </summary>
        [TestMethod]
        public void TestUManifestResponseForAKeyWithoutManifest()
        {
            const string body = """
                {
                  "details": {
                    "ephemeral_key": "n9LpenCtYuVqRouZbJmKix3iyvoEB8Qsq4x1MHyCQr6Y4kDBStuP",
                    "master_key": "n9LpenCtYuVqRouZbJmKix3iyvoEB8Qsq4x1MHyCQr6Y4kDBStuP"
                  },
                  "requested": "n9LpenCtYuVqRouZbJmKix3iyvoEB8Qsq4x1MHyCQr6Y4kDBStuP"
                }
                """;

            ManifestResponse response = Read<ManifestResponse>(body);

            Assert.AreEqual("n9LpenCtYuVqRouZbJmKix3iyvoEB8Qsq4x1MHyCQr6Y4kDBStuP", response.Requested);
            Assert.IsNull(response.Manifest);
            Assert.AreEqual(response.Requested, response.Details.MasterKey);
            Assert.AreEqual(response.Requested, response.Details.EphemeralKey);
            Assert.IsNull(response.Details.Seq);
            Assert.IsNull(response.Details.Domain);
        }

        [TestMethod]
        public void TestUVersionRequestTakesNoParameters()
        {
            string json = Serialize(new VersionRequest());

            StringAssert.Contains(json, "\"command\":\"version\"");
        }

        /// <summary>
        /// Asked with API version 2 or later, rippled answers with the version numbers.
        /// </summary>
        [TestMethod]
        public void TestUVersionResponseToApiV2HasNumbers()
        {
            VersionResponse response = Read<VersionResponse>("""{ "version": { "first": 1, "last": 2 } }""");

            Assert.AreEqual("1", response.Version.First);
            Assert.AreEqual("2", response.Version.Last);
            Assert.IsNull(response.Version.Good);
        }

        /// <summary>
        /// Asked with API version 1, rippled answers with the legacy strings, including <c>good</c>.
        /// </summary>
        [TestMethod]
        public void TestUVersionResponseToApiV1HasLegacyStrings()
        {
            VersionResponse response = Read<VersionResponse>("""{ "version": { "first": "1.0.0", "good": "1.0.0", "last": "1.0.0" } }""");

            Assert.AreEqual("1.0.0", response.Version.First);
            Assert.AreEqual("1.0.0", response.Version.Good);
            Assert.AreEqual("1.0.0", response.Version.Last);
        }
    }
}
