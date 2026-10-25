using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Ledger entries whose STArray fields rippled renders as <c>[{"InnerName": {...}}]</c>, read in
    /// the shapes <c>ledger_entry</c> and transaction metadata return.
    /// </summary>
    [TestClass]
    public class TestULedgerInnerObjectArrays
    {
        private static readonly DateTime RippleEpoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // The xrpl.org example AMM entry (mainnet index F490627B...), with an AuctionSlot that also
        // carries AuthAccounts and DiscountedFee, as rippled writes them after an AMMBid naming them.
        private const string AmmEntry = """
            {
                "Account": "rBp3UDRuEteeJqp4rEk5kxMe7BGWNYrF9A",
                "Asset": { "currency": "XRP" },
                "Asset2": { "currency": "NEX", "issuer": "rQGiPFWhaTDdue1xHX7cVpxGqPQK54zng1" },
                "AuctionSlot": {
                    "Account": "r3ZGQZw1NCbBp5AEGkMDE9NgNpzw91aofD",
                    "AuthAccounts": [
                        { "AuthAccount": { "Account": "rMKXGCbJ5d8LbrqthdG46q3f969MVK2Qeg" } },
                        { "AuthAccount": { "Account": "rBepJuTLFJt3WmtLXYAxSjtBWAeQxVbncv" } }
                    ],
                    "DiscountedFee": 100,
                    "Expiration": 778576560,
                    "Price": {
                        "currency": "03DC324562A8915B7C65E9D31B93D62D02BC491C",
                        "issuer": "rBp3UDRuEteeJqp4rEk5kxMe7BGWNYrF9A",
                        "value": "0"
                    }
                },
                "Flags": 0,
                "LPTokenBalance": {
                    "currency": "03DC324562A8915B7C65E9D31B93D62D02BC491C",
                    "issuer": "rBp3UDRuEteeJqp4rEk5kxMe7BGWNYrF9A",
                    "value": "5509581.299648495"
                },
                "LedgerEntryType": "AMM",
                "OwnerNode": "0",
                "PreviousTxnID": "9E8E9B8FD27391C818525BFF6A29452F7A9888F31622BEF6FC36064D05CF6436",
                "PreviousTxnLgrSeq": 91448830,
                "TradingFee": 1,
                "VoteSlots": [
                    { "VoteEntry": { "Account": "r3ZGQZw1NCbBp5AEGkMDE9NgNpzw91aofD", "TradingFee": 1, "VoteWeight": 100000 } },
                    { "VoteEntry": { "Account": "rMKXGCbJ5d8LbrqthdG46q3f969MVK2Qeg", "TradingFee": 600, "VoteWeight": 2500 } }
                ],
                "index": "F490627BACE2D0AA744514A640B4999D50E495DD1677550D8B10E2D20FBB15C3"
            }
            """;

        [TestMethod]
        public void Amm_AuctionSlot_ReadsTheLedgerShape()
        {
            LOAmm amm = JsonSerializer.Deserialize<LOAmm>(AmmEntry, XrplJsonOptions.Default);

            Assert.IsNotNull(amm.AuctionSlot);
            Assert.AreEqual("r3ZGQZw1NCbBp5AEGkMDE9NgNpzw91aofD", amm.AuctionSlot.Account);
            Assert.AreEqual(100u, amm.AuctionSlot.DiscountedFee);
            Assert.AreEqual(RippleEpoch.AddSeconds(778576560), amm.AuctionSlot.Expiration);
            Assert.AreEqual("0", amm.AuctionSlot.Price.Value);
            Assert.AreEqual("rBp3UDRuEteeJqp4rEk5kxMe7BGWNYrF9A", amm.AuctionSlot.Price.Issuer);
            Assert.AreEqual(2, amm.AuctionSlot.AuthAccounts.Count);
            Assert.AreEqual("rMKXGCbJ5d8LbrqthdG46q3f969MVK2Qeg", amm.AuctionSlot.AuthAccounts[0].Account);
            Assert.AreEqual("rBepJuTLFJt3WmtLXYAxSjtBWAeQxVbncv", amm.AuctionSlot.AuthAccounts[1].Account);
            Assert.IsNull(amm.AuctionSlot.UnknownFields, "every field of the slot is declared");
        }

        [TestMethod]
        public void Amm_VoteSlots_UnwrapsEachVoteEntry()
        {
            LOAmm amm = JsonSerializer.Deserialize<LOAmm>(AmmEntry, XrplJsonOptions.Default);

            Assert.AreEqual(2, amm.VoteSlots.Count);
            Assert.AreEqual("r3ZGQZw1NCbBp5AEGkMDE9NgNpzw91aofD", amm.VoteSlots[0].Account);
            Assert.AreEqual(1u, amm.VoteSlots[0].TradingFee);
            Assert.AreEqual(100000u, amm.VoteSlots[0].VoteWeight);
            Assert.AreEqual("rMKXGCbJ5d8LbrqthdG46q3f969MVK2Qeg", amm.VoteSlots[1].Account);
            Assert.AreEqual(600u, amm.VoteSlots[1].TradingFee);
            Assert.AreEqual(2500u, amm.VoteSlots[1].VoteWeight);
            Assert.IsNull(amm.VoteSlots[0].UnknownFields);
        }

        /// <summary>
        /// DiscountedFee is soeDEFAULT in rippled's AuctionSlot format, so a slot won with a zero
        /// discounted fee carries no such field, and no AuthAccounts when none were named.
        /// </summary>
        [TestMethod]
        public void Amm_AuctionSlotWithoutOptionalFields_ReadsDefaults()
        {
            const string json = """
                {
                    "LedgerEntryType": "AMM",
                    "AuctionSlot": {
                        "Account": "r3ZGQZw1NCbBp5AEGkMDE9NgNpzw91aofD",
                        "Expiration": 0,
                        "Price": { "currency": "03DC324562A8915B7C65E9D31B93D62D02BC491C", "issuer": "rBp3UDRuEteeJqp4rEk5kxMe7BGWNYrF9A", "value": "0" }
                    }
                }
                """;

            LOAmm amm = JsonSerializer.Deserialize<LOAmm>(json, XrplJsonOptions.Default);

            Assert.AreEqual(0u, amm.AuctionSlot.DiscountedFee);
            Assert.IsNull(amm.AuctionSlot.AuthAccounts);
            Assert.AreEqual(RippleEpoch, amm.AuctionSlot.Expiration);
            Assert.IsNull(amm.VoteSlots);
        }

        /// <summary>
        /// An AMM node in transaction metadata (AffectedNodes / ModifiedNode) carries the same
        /// FinalFields shape as <c>ledger_entry</c>.
        /// </summary>
        [TestMethod]
        public void Amm_FinalFieldsOfAModifiedNode_ReadTheSameShape()
        {
            JsonNode node = JsonNode.Parse($$"""
                {
                    "ModifiedNode": {
                        "FinalFields": {{AmmEntry}},
                        "LedgerEntryType": "AMM",
                        "LedgerIndex": "F490627BACE2D0AA744514A640B4999D50E495DD1677550D8B10E2D20FBB15C3"
                    }
                }
                """);

            LOAmm amm = node["ModifiedNode"]["FinalFields"].Deserialize<LOAmm>(XrplJsonOptions.Default);

            Assert.AreEqual(100u, amm.AuctionSlot.DiscountedFee);
            Assert.AreEqual(2, amm.AuctionSlot.AuthAccounts.Count);
            Assert.AreEqual(2, amm.VoteSlots.Count);
        }

        [TestMethod]
        public void Amm_RoundTrip_WritesTheWrappers()
        {
            LOAmm amm = JsonSerializer.Deserialize<LOAmm>(AmmEntry, XrplJsonOptions.Default);

            JsonNode written = JsonNode.Parse(JsonSerializer.Serialize(amm, XrplJsonOptions.Default));

            Assert.AreEqual(600, written["VoteSlots"][1]["VoteEntry"]["TradingFee"].GetValue<int>());
            Assert.AreEqual("rBepJuTLFJt3WmtLXYAxSjtBWAeQxVbncv", written["AuctionSlot"]["AuthAccounts"][1]["AuthAccount"]["Account"].GetValue<string>());
            Assert.AreEqual(778576560, written["AuctionSlot"]["Expiration"].GetValue<long>());
            Assert.AreEqual(100, written["AuctionSlot"]["DiscountedFee"].GetValue<int>());
        }

        [TestMethod]
        public void Amendments_Majorities_UnwrapsEachMajority()
        {
            // xrpl.org example Amendments entry, enabled list shortened.
            const string json = """
                {
                    "Amendments": [
                        "42426C4D4F1009EE67080A9B7965B44656D7714D104A72F9B4369F97ABF044EE"
                    ],
                    "Flags": 0,
                    "LedgerEntryType": "Amendments",
                    "Majorities": [
                        { "Majority": { "Amendment": "7BB62DC13EC72B775091E9C71BF8CF97E122647693B50C5E87A80DFD6FCFAC50", "CloseTime": 779561310 } },
                        { "Majority": { "Amendment": "755C971C29971C9F20C6F080F2ED96F87884E40AD19554A5EBECDCEC8A1F77FE", "CloseTime": 779561320 } }
                    ],
                    "index": "7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4"
                }
                """;

            LOAmendments amendments = JsonSerializer.Deserialize<LOAmendments>(json, XrplJsonOptions.Default);

            Assert.AreEqual(2, amendments.Majorities.Count);
            Assert.AreEqual("7BB62DC13EC72B775091E9C71BF8CF97E122647693B50C5E87A80DFD6FCFAC50", amendments.Majorities[0].Amendment);
            Assert.AreEqual(RippleEpoch.AddSeconds(779561310), amendments.Majorities[0].CloseTime);
            Assert.AreEqual("755C971C29971C9F20C6F080F2ED96F87884E40AD19554A5EBECDCEC8A1F77FE", amendments.Majorities[1].Amendment);
            Assert.AreEqual(RippleEpoch.AddSeconds(779561320), amendments.Majorities[1].CloseTime);

            JsonNode written = JsonNode.Parse(JsonSerializer.Serialize(amendments, XrplJsonOptions.Default));
            Assert.AreEqual(779561320, written["Majorities"][1]["Majority"]["CloseTime"].GetValue<long>());
        }

        [TestMethod]
        public void NegativeUNL_DisabledValidators_UnwrapsEachDisabledValidator()
        {
            // xrpl.org example NegativeUNL entry.
            const string json = """
                {
                    "DisabledValidators": [
                        { "DisabledValidator": { "FirstLedgerSequence": 91371264, "PublicKey": "ED58F6770DB5DD77E59D28CB650EC3816E2FC95021BB56E720C9A12DA79C58A3AB" } }
                    ],
                    "Flags": 0,
                    "LedgerEntryType": "NegativeUNL",
                    "PreviousTxnID": "8D47FFE664BE6C335108DF689537625855A6A95160CC6D351341B92624D9C5E3",
                    "PreviousTxnLgrSeq": 91442944,
                    "index": "2E8A59AA9D3B5B186B0B9E0F62E6C02587CA74A4D778938E957B6357D364B244"
                }
                """;

            LONegativeUNL nunl = JsonSerializer.Deserialize<LONegativeUNL>(json, XrplJsonOptions.Default);

            Assert.AreEqual(1, nunl.DisabledValidators.Count);
            Assert.AreEqual(91371264L, nunl.DisabledValidators[0].FirstLedgerSequence);
            Assert.AreEqual("ED58F6770DB5DD77E59D28CB650EC3816E2FC95021BB56E720C9A12DA79C58A3AB", nunl.DisabledValidators[0].PublicKey);
        }

        /// <summary>
        /// A list in the flat shape - built by hand, or written by a version of this SDK that
        /// did not wrap - still binds.
        /// </summary>
        [TestMethod]
        public void InnerObjectList_FlatElement_IsReadAsTheInnerObject()
        {
            const string json = """
                { "LedgerEntryType": "NegativeUNL", "DisabledValidators": [ { "FirstLedgerSequence": 5, "PublicKey": "ED00" } ] }
                """;

            LONegativeUNL nunl = JsonSerializer.Deserialize<LONegativeUNL>(json, XrplJsonOptions.Default);

            Assert.AreEqual(5L, nunl.DisabledValidators[0].FirstLedgerSequence);
            Assert.AreEqual("ED00", nunl.DisabledValidators[0].PublicKey);
        }

        [TestMethod]
        public void InnerObjectList_NotAnArray_Throws()
        {
            const string json = """{ "LedgerEntryType": "NegativeUNL", "DisabledValidators": {} }""";

            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<LONegativeUNL>(json, XrplJsonOptions.Default));
        }
    }
}
