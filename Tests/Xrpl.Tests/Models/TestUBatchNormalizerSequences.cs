using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Utils;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// How NormalizeBatchTransaction fills Sequence on the outer Batch and its inner transactions.
    /// rippled's Batch preflight wants each inner to carry exactly one of a non-zero Sequence and a
    /// TicketSequence, and a ticketed outer consumes no account sequence (temSEQ_AND_TICKET otherwise).
    /// </summary>
    [TestClass]
    public class TestUBatchNormalizerSequences
    {
        private const string Alice = "rMFMJsQaKEMEwwMRBHCoNXkFswJrWyNYLp";
        private const string Bob = "rJcvzBFCTcCrQdTLPZoRXJEboT9C3Wrd6H";

        private static readonly Dictionary<string, uint> LedgerSequences = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            [Alice] = 10,
            [Bob] = 20,
        };

        private static Dictionary<string, object> Inner(string account, Dictionary<string, object>? extra = null)
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                ["TransactionType"] = "Payment",
                ["Account"] = account,
                ["Destination"] = account == Alice ? Bob : Alice,
                ["Amount"] = "1000",
            };
            if (extra != null)
            {
                foreach (KeyValuePair<string, object> kv in extra)
                    tx[kv.Key] = kv.Value;
            }
            return new Dictionary<string, object> { ["RawTransaction"] = tx };
        }

        private static Dictionary<string, object> Outer(Dictionary<string, object>? extra, params Dictionary<string, object>[] inners)
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                ["TransactionType"] = "Batch",
                ["Account"] = Alice,
                ["Flags"] = 0x00010000u,
                ["RawTransactions"] = new List<Dictionary<string, object>>(inners),
            };
            if (extra != null)
            {
                foreach (KeyValuePair<string, object> kv in extra)
                    tx[kv.Key] = kv.Value;
            }
            return tx;
        }

        private static Dictionary<string, object> RawAt(Dictionary<string, object> outer, int index)
        {
            List<Dictionary<string, object>> raws = (List<Dictionary<string, object>>)outer["RawTransactions"];
            return (Dictionary<string, object>)raws[index]["RawTransaction"];
        }

        private static uint SequenceOf(Dictionary<string, object> tx)
            => Convert.ToUInt32(tx["Sequence"], CultureInfo.InvariantCulture);

        [TestMethod]
        public async Task TestUNormalizeBatch_TicketedOuter_GetsSequenceZeroAndLeavesTheAccountSequenceToTheInners()
        {
            IXrplClient client = SequenceClient.Create(LedgerSequences);
            Dictionary<string, object> tx = Outer(
                new Dictionary<string, object> { ["TicketSequence"] = 5u },
                Inner(Alice),
                Inner(Bob),
                Inner(Alice));

            await client.NormalizeBatchTransaction(tx);

            Assert.AreEqual(0u, SequenceOf(tx));
            Assert.AreEqual(5u, Convert.ToUInt32(tx["TicketSequence"], CultureInfo.InvariantCulture));
            Assert.AreEqual(10u, SequenceOf(RawAt(tx, 0)));
            Assert.AreEqual(20u, SequenceOf(RawAt(tx, 1)));
            Assert.AreEqual(11u, SequenceOf(RawAt(tx, 2)));
        }

        [TestMethod]
        public async Task TestUNormalizeBatch_SequencedOuter_InnersOfTheSameAccountStartOnePastIt()
        {
            IXrplClient client = SequenceClient.Create(LedgerSequences);
            Dictionary<string, object> tx = Outer(null, Inner(Alice), Inner(Bob));

            await client.NormalizeBatchTransaction(tx);

            Assert.AreEqual(10u, SequenceOf(tx));
            Assert.AreEqual(11u, SequenceOf(RawAt(tx, 0)));
            Assert.AreEqual(20u, SequenceOf(RawAt(tx, 1)));
        }

        [TestMethod]
        public async Task TestUNormalizeBatch_TicketedInner_GetsSequenceZeroAndConsumesNoSequence()
        {
            IXrplClient client = SequenceClient.Create(LedgerSequences);
            Dictionary<string, object> tx = Outer(
                null,
                Inner(Bob, new Dictionary<string, object> { ["TicketSequence"] = 7u }),
                Inner(Bob));

            await client.NormalizeBatchTransaction(tx);

            Dictionary<string, object> ticketed = RawAt(tx, 0);
            Assert.AreEqual(0u, SequenceOf(ticketed));
            Assert.AreEqual(7u, Convert.ToUInt32(ticketed["TicketSequence"], CultureInfo.InvariantCulture));
            Assert.AreEqual(20u, SequenceOf(RawAt(tx, 1)));
        }

        [TestMethod]
        public async Task TestUNormalizeBatch_CallerInnerSequence_IsKeptAndLaterInnersContinueAfterIt()
        {
            IXrplClient client = SequenceClient.Create(LedgerSequences);
            Dictionary<string, object> tx = Outer(
                null,
                Inner(Bob, new Dictionary<string, object> { ["Sequence"] = 42u }),
                Inner(Bob));

            await client.NormalizeBatchTransaction(tx);

            Assert.AreEqual(42u, SequenceOf(RawAt(tx, 0)));
            Assert.AreEqual(43u, SequenceOf(RawAt(tx, 1)));
        }

        [TestMethod]
        public async Task TestUNormalizeBatch_CallerSequenceBelowTheCounter_DoesNotMoveItBack()
        {
            IXrplClient client = SequenceClient.Create(LedgerSequences);
            Dictionary<string, object> tx = Outer(
                null,
                Inner(Bob),
                Inner(Bob, new Dictionary<string, object> { ["Sequence"] = 3u }),
                Inner(Bob));

            await client.NormalizeBatchTransaction(tx);

            Assert.AreEqual(20u, SequenceOf(RawAt(tx, 0)));
            Assert.AreEqual(3u, SequenceOf(RawAt(tx, 1)));
            Assert.AreEqual(21u, SequenceOf(RawAt(tx, 2)));
        }

        [TestMethod]
        public async Task TestUNormalizeBatch_CallerSequenceBelowTheLedger_StillReadsTheLedger()
        {
            IXrplClient client = SequenceClient.Create(LedgerSequences);
            Dictionary<string, object> tx = Outer(
                null,
                Inner(Bob, new Dictionary<string, object> { ["Sequence"] = 3u }),
                Inner(Bob));

            await client.NormalizeBatchTransaction(tx);

            Assert.AreEqual(3u, SequenceOf(RawAt(tx, 0)));
            Assert.AreEqual(20u, SequenceOf(RawAt(tx, 1)));
        }

        [TestMethod]
        public async Task TestUNormalizeBatch_CallerSequenceAtUInt32Max_RaisesValidationException()
        {
            IXrplClient client = SequenceClient.Create(LedgerSequences);
            Dictionary<string, object> tx = Outer(
                null,
                Inner(Bob, new Dictionary<string, object> { ["Sequence"] = uint.MaxValue }),
                Inner(Bob));

            await Assert.ThrowsExactlyAsync<ValidationException>(() => client.NormalizeBatchTransaction(tx));
        }

        /// <summary>
        /// IXrplClient answering only account_info, with a fixed Sequence per account. Anything else
        /// the normaliser might call fails the test loudly.
        /// </summary>
        public class SequenceClient : DispatchProxy
        {
            private IReadOnlyDictionary<string, uint> _sequences = new Dictionary<string, uint>();

            public static IXrplClient Create(IReadOnlyDictionary<string, uint> sequences)
            {
                IXrplClient proxy = Create<IXrplClient, SequenceClient>();
                ((SequenceClient)(object)proxy)._sequences = sequences;
                return proxy;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod?.Name != nameof(IXrplClient.AccountInfo))
                    throw new NotSupportedException(targetMethod?.Name);

                AccountInfoRequest request = (AccountInfoRequest)args![0]!;
                AccountInfo info = new AccountInfo
                {
                    AccountData = new LOAccountRoot { Sequence = _sequences[request.Account] },
                };
                return Task.FromResult(new XrplResponse<AccountInfo>(info, default, null, null, null, null, false));
            }
        }
    }
}
