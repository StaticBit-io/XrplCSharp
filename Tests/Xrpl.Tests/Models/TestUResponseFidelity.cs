using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Round-trips the live mainnet response corpus (<c>Fixtures/Responses</c>) through
    /// deserialize -> serialize and structurally diffs the result against the node's own bytes.
    /// </summary>
    /// <remarks>
    /// Levels 0-3 removed fabricated members, silently dropped members and v1/v2 name
    /// substitution from this SDK's response models. Every one of those measurements was taken
    /// by hand, with a throwaway console project that got deleted afterwards - so the 156-member
    /// fabrication count on a ten-transaction <c>account_tx</c> becoming 0 was a one-time fact,
    /// not a standing guarantee. This class is what turns it into one: run on every push, it
    /// fails the moment a new nullable-vs-required regression or a removed
    /// <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/> reintroduces either
    /// defect. See <c>Fixtures/Responses/README.md</c> for corpus provenance and
    /// <c>plans/2026-08-17-raw-response-level4.md</c> for the design rationale.
    /// <para>
    /// Two different failure classes, on purpose:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Fabricated (added) members</b> - the model serialized something the node never sent.
    /// Zero tolerance, no exceptions table: a fabricated member is always a lie about what the
    /// node said, which is exactly the defect level 3 spent its effort removing (the last
    /// instance was <c>Amount</c> standing in for <c>DeliverMax</c>).
    /// </description></item>
    /// <item><description>
    /// <b>Dropped (lost) members</b> - the model does not carry something the node sent. This is
    /// a known, bounded limitation of projecting onto typed models rather than a defect on the
    /// same footing as a fabrication: a caller who needs full fidelity already has
    /// <c>XrplResponse&lt;T&gt;.Raw</c>, the exact bytes the node sent, untouched by any model.
    /// Every drop this test accepts is named in <see cref="KnownLostMembers"/> with a reason;
    /// anything not listed there fails the test.
    /// </description></item>
    /// </list>
    /// </remarks>
    [TestClass]
    public class TestUResponseFidelity
    {
        private static readonly string ResponsesDirectory =
            System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Responses");

        /// <summary>
        /// Corpus file -> the model type XrplClient actually deserializes that command's
        /// <c>result</c> into. Every <c>.json</c> file under <c>Fixtures/Responses</c> must
        /// appear here - <see cref="TestUEveryCorpusFileHasAModelMapping"/> fails the build on
        /// any file this dictionary does not cover, so a fixture added to the corpus without a
        /// mapping cannot silently go unchecked.
        /// </summary>
        private static readonly Dictionary<string, Type> Models = new(StringComparer.Ordinal)
        {
            // tx, JSON mode and binary mode alike. rippled has no dedicated tx response shape of
            // its own (see the "todo not found class TxResponse" note in Models/Methods/Tx.cs) -
            // a `tx` result's field set (close_time_iso, ctid, hash, ledger_hash, ledger_index,
            // meta/meta_blob, tx_json/tx_blob, validated) is exactly what TransactionSummary
            // already models for account_tx's per-transaction entries, so IXrplClient.TxV2 reuses
            // it verbatim (GRequest<TransactionSummary, TxRequest> in Client/IXrplClient.cs).
            ["tx_raw.json"] = typeof(TransactionSummary),
            ["tx_binary_raw.json"] = typeof(TransactionSummary),
            // api_version: 1. rippled's `tx` method has no dedicated v1 response shape either - a v1
            // result is the transaction's own fields (Account, Amount, Destination, meta, ...) sitting
            // directly at the top of `result`, which is exactly what TransactionResponse models (see
            // IXrplClient.TxV1, GRequest<TransactionResponse, TxRequest>). TransactionResponse itself
            // carries [JsonConverter(typeof(TransactionResponseConverter))] and dispatches on
            // TransactionType, so a Payment here deserializes as PaymentResponse - the class this file
            // was captured to exercise (see the table below).
            ["tx_v1_raw.json"] = typeof(TransactionResponse),
            ["account_tx_raw.json"] = typeof(AccountTransactions),
            ["account_info_raw.json"] = typeof(AccountInfo),
            ["account_objects_raw.json"] = typeof(AccountObjects),
            ["ledger_raw.json"] = typeof(LOLedger),
        };

        /// <summary>
        /// Corpus file -> member path -> why the model does not carry it. Paths use
        /// <c>$.name</c>/<c>[index]</c> notation rooted at the command's <c>result</c>. A lost
        /// member absent from its file's table fails <see cref="TestUCorpusRoundTripIsFaithful"/>.
        /// </summary>
        /// <remarks>
        /// The reason must say *why* the field is unmodeled, not restate that it is unmodeled -
        /// "no property carries it" is not a reason, it is the finding itself.
        /// </remarks>
        private static readonly Dictionary<string, Dictionary<string, string>> KnownLostMembers =
            new(StringComparer.Ordinal)
            {
                ["tx_raw.json"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["$.status"] = StatusReason,
                },
                ["tx_binary_raw.json"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["$.status"] = StatusReason,
                },
            };

        /// <summary>
        /// Why <c>tx_raw.json</c>/<c>tx_binary_raw.json</c> still drop <c>$.status</c> even after
        /// <see cref="Methods.BaseMethodResult"/> and <see cref="Ledger.LOLedger.UnknownFields"/>
        /// stopped account_info/account_objects/account_tx/ledger from dropping it: both files
        /// deserialize into <see cref="Methods.TransactionSummary"/> (see <see cref="Models"/>
        /// above), which has no <c>[JsonExtensionData]</c> of its own - it models a `tx`/`account_tx`
        /// entry, not a full command result, and was out of scope for the unknown-field pass that
        /// added the other four. Verified experimentally (not assumed): removing every other file's
        /// entry from this table left <see cref="TestUCorpusRoundTripIsFaithful"/> green, proving
        /// `status` now round-trips there through <c>UnknownFields</c> instead of vanishing - status
        /// genuinely still lives outside `result` on the WebSocket envelope
        /// (<c>XrplResponse&lt;T&gt;.Status</c>), but these HTTP JSON-RPC fixtures nest it inside
        /// `result`, and a model with extension-data capture now keeps it rather than dropping it.
        /// </summary>
        private const string StatusReason =
            "Lives outside `result` on the WebSocket envelope XrplClient actually parses - it "
            + "arrives through XrplResponse<T>.Status, a sibling of Result, not a member of it. "
            + "These fixtures were captured over HTTP JSON-RPC, where rippled nests status inside "
            + "result instead. TransactionSummary (what tx_raw.json/tx_binary_raw.json deserialize "
            + "into) has no [JsonExtensionData] of its own, so status is genuinely dropped here - "
            + "unlike account_info/account_objects/account_tx/ledger, which now carry it through "
            + "BaseMethodResult.UnknownFields / LOLedger.UnknownFields instead of losing it.";

        /// <summary>
        /// Guards <see cref="Models"/> itself: a corpus file with no entry here would otherwise
        /// just be skipped by <see cref="TestUCorpusRoundTripIsFaithful"/>, silently exempting it
        /// from fidelity checking instead of failing loudly.
        /// </summary>
        [TestMethod]
        public void TestUEveryCorpusFileHasAModelMapping()
        {
            string[] corpusFiles = Directory.GetFiles(ResponsesDirectory, "*.json");
            Assert.IsTrue(corpusFiles.Length > 0, $"no .json fixtures found under {ResponsesDirectory}");

            List<string> unmapped = corpusFiles
                .Select(System.IO.Path.GetFileName)
                .Where(name => !Models.ContainsKey(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.AreEqual(
                0,
                unmapped.Count,
                "corpus file(s) added without a Models[] mapping - they would silently skip fidelity "
                    + "checking entirely: " + string.Join(", ", unmapped));
        }

        /// <summary>
        /// The core check: for every mapped corpus file, deserialize <c>result</c> into its model
        /// and serialize back, then structurally diff against the original <c>result</c>.
        /// Fabricated members always fail; dropped members fail unless named in
        /// <see cref="KnownLostMembers"/>.
        /// </summary>
        [TestMethod]
        public void TestUCorpusRoundTripIsFaithful()
        {
            List<string> failures = new List<string>();

            foreach (KeyValuePair<string, Type> entry in Models.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string file = entry.Key;
                Type modelType = entry.Value;
                string fixturePath = System.IO.Path.Combine(ResponsesDirectory, file);

                Assert.IsTrue(File.Exists(fixturePath), $"{file}: mapped in Models but the fixture file is missing at {fixturePath}");

                JsonNode envelope = JsonNode.Parse(File.ReadAllText(fixturePath));
                JsonNode original = envelope?["result"];
                Assert.IsNotNull(original, $"{file}: no top-level \"result\" member - malformed fixture");

                object model = original.Deserialize(modelType, XrplJsonOptions.Default);
                Assert.IsNotNull(model, $"{file}: deserializing result as {modelType.Name} produced null");

                string roundTrippedJson = JsonSerializer.Serialize(model, modelType, XrplJsonOptions.Default);
                JsonNode roundTripped = JsonNode.Parse(roundTrippedJson);

                List<string> added = new List<string>();
                List<string> lost = new List<string>();
                DiffMembers(original, roundTripped, "$", added, lost);

                KnownLostMembers.TryGetValue(file, out Dictionary<string, string> knownForFile);
                knownForFile ??= new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (string addedPath in added.OrderBy(p => p, StringComparer.Ordinal))
                {
                    failures.Add($"{file} [{modelType.Name}]: FABRICATED {addedPath} "
                        + "- the model serialized a member the node never sent");
                }

                foreach (string lostPath in lost.OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (!knownForFile.ContainsKey(lostPath))
                    {
                        failures.Add($"{file} [{modelType.Name}]: DROPPED {lostPath} "
                            + "- not present in KnownLostMembers for this file; either the model "
                            + "should carry it, or the drop needs a documented reason");
                    }
                }
            }

            Assert.AreEqual(
                0,
                failures.Count,
                "response fidelity regression - fabricated members must never happen, and dropped "
                    + "members must be named in KnownLostMembers with a reason:"
                    + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// Recursively compares two <see cref="JsonNode"/> trees rooted at the same logical
        /// position and records every member path present on only one side.
        /// </summary>
        /// <remarks>
        /// Deliberately member-presence only, not value equality: XrplJsonOptions round-trips
        /// numeric/string representations through several converters (currency amounts, dates,
        /// hex blobs) that can legitimately change a leaf's textual form without changing what it
        /// means. This test's job is "did a member appear or vanish", not "is every value
        /// byte-identical" - value fidelity for callers who need it is what
        /// <c>XrplResponse&lt;T&gt;.Raw</c> is for.
        /// </remarks>
        private static void DiffMembers(JsonNode original, JsonNode roundTripped, string path, List<string> added, List<string> lost)
        {
            JsonObject originalObject = original as JsonObject;
            JsonObject roundTrippedObject = roundTripped as JsonObject;
            if (originalObject is not null || roundTrippedObject is not null)
            {
                if (originalObject is not null)
                {
                    foreach (KeyValuePair<string, JsonNode> member in originalObject)
                    {
                        string childPath = $"{path}.{member.Key}";
                        if (roundTrippedObject is not null && roundTrippedObject.TryGetPropertyValue(member.Key, out JsonNode roundTrippedChild))
                        {
                            DiffMembers(member.Value, roundTrippedChild, childPath, added, lost);
                        }
                        else
                        {
                            lost.Add(childPath);
                        }
                    }
                }

                if (roundTrippedObject is not null)
                {
                    foreach (KeyValuePair<string, JsonNode> member in roundTrippedObject)
                    {
                        if (originalObject is null || !originalObject.ContainsKey(member.Key))
                        {
                            added.Add($"{path}.{member.Key}");
                        }
                    }
                }

                return;
            }

            JsonArray originalArray = original as JsonArray;
            JsonArray roundTrippedArray = roundTripped as JsonArray;
            if (originalArray is not null || roundTrippedArray is not null)
            {
                int originalCount = originalArray?.Count ?? 0;
                int roundTrippedCount = roundTrippedArray?.Count ?? 0;
                int commonCount = Math.Min(originalCount, roundTrippedCount);

                for (int index = 0; index < commonCount; index++)
                {
                    DiffMembers(originalArray[index], roundTrippedArray[index], $"{path}[{index}]", added, lost);
                }

                for (int index = commonCount; index < originalCount; index++)
                {
                    lost.Add($"{path}[{index}]");
                }

                for (int index = commonCount; index < roundTrippedCount; index++)
                {
                    added.Add($"{path}[{index}]");
                }

                return;
            }

            // Both sides are leaves (string/number/bool/null) or absent - member presence is
            // already resolved by the caller; leaf value equality is out of scope (see remarks).
        }
    }
}
