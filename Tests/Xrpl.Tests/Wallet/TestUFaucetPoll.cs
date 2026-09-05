using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Exceptions;
using Xrpl.Wallet;

using XrplTests.Xrpl.Sugar;

namespace Xrpl.Tests.Wallet.Tests
{
    /// <summary>
    /// What the wait for a faucet payment reports when it ends without one. Not being able to
    /// read a balance is the normal case at first - the account is not on the ledger until the
    /// payment validates - so the poll keeps going. The question these ask is what is left to
    /// say when it never rises.
    /// </summary>
    [TestClass]
    public class TestUFaucetPoll
    {
        /// <summary>Answers the scripted sequence, one entry per call: a balance or a throw.</summary>
        private sealed class ScriptedBalanceClient : FeeTestClient
        {
            private readonly Queue<Func<string>> _answers;

            public ScriptedBalanceClient(params Func<string>[] answers) : base("0.00001", 2)
            {
                _answers = new Queue<Func<string>>(answers);
            }

            public int Calls { get; private set; }

            public override Task<string> GetXrpBalance(string address, CancellationToken cancellationToken = default)
            {
                Calls++;
                // The last entry repeats: a test about a poll that never got an answer needs
                // every attempt to fail, not just the scripted ones
                Func<string> answer = _answers.Count > 1 ? _answers.Dequeue() : _answers.Peek();
                return Task.FromResult(answer());
            }
        }

        private static Func<string> Throws(Exception err) => () => throw err;

        [TestMethod]
        public async Task Poll_WhenTheBalanceRises_ReportsItWithNoFailure()
        {
            ScriptedBalanceClient client = new ScriptedBalanceClient(() => "100");

            WalletSugar.PollOutcome outcome = await WalletSugar.PollForFundedBalance(client, "rTest", 0, default, attempts: 3, intervalSeconds: 0);

            Assert.AreEqual(100d, outcome.Balance);
            Assert.IsNull(outcome.LastReadFailure);
        }

        /// <summary>
        /// Every read failed, so the balance never rose - and the reason it never rose is the
        /// read failure, not the faucet. Dropping it leaves a message that blames the faucet
        /// for a client that was disconnected the whole time.
        /// </summary>
        [TestMethod]
        public async Task Poll_WhenEveryReadFailed_KeepsTheLastFailure()
        {
            DisconnectedException dropped = new DisconnectedException("websocket closed");
            ScriptedBalanceClient client = new ScriptedBalanceClient(Throws(new DisconnectedException("first")), Throws(dropped));

            WalletSugar.PollOutcome outcome = await WalletSugar.PollForFundedBalance(client, "rTest", 0, default, attempts: 3, intervalSeconds: 0);

            Assert.AreEqual(0d, outcome.Balance);
            Assert.AreSame(dropped, outcome.LastReadFailure, "the last failure is the one that describes the end state");
        }

        /// <summary>
        /// A read that succeeded and simply showed no money is not a failure to read, so the
        /// earlier failure must not be reported as the reason the wait ended.
        /// </summary>
        [TestMethod]
        public async Task Poll_WhenAReadSucceedsAfterAFailure_ForgetsTheFailure()
        {
            ScriptedBalanceClient client = new ScriptedBalanceClient(Throws(new DisconnectedException("transient")), () => "0");

            WalletSugar.PollOutcome outcome = await WalletSugar.PollForFundedBalance(client, "rTest", 0, default, attempts: 3, intervalSeconds: 0);

            Assert.AreEqual(0d, outcome.Balance);
            Assert.IsNull(outcome.LastReadFailure, "the ledger answered - it just had nothing to report");
        }

        [TestMethod]
        public async Task Poll_HonoursCancellationRatherThanWaitingOutItsBudget()
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();
            ScriptedBalanceClient client = new ScriptedBalanceClient(() => "100");

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(
                () => WalletSugar.PollForFundedBalance(client, "rTest", 0, cts.Token, attempts: 3, intervalSeconds: 0));

            Assert.AreEqual(0, client.Calls, "a cancelled poll must not reach the node at all");
        }
    }
}
