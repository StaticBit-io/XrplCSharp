using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace Xrpl.Tests.Sugar
{
    [TestClass]
    public class TestUSubmitMultiAndDeleteBlockers
    {
        private const string Account = "rPEPPER7kfTD9w2To4CQk6UCfuHM9c6GDY";

        private static Dictionary<string, object> UnsignedPayment() => new Dictionary<string, object>
        {
            ["TransactionType"] = "Payment",
            ["Account"] = Account,
            ["Destination"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            ["Amount"] = "1000000",
            ["Fee"] = "36",
            ["Sequence"] = 1u,
            ["Flags"] = 0u,
            ["SigningPubKey"] = "",
        };

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task TestUSubmitMulti_PassesFailHard(bool failHard)
        {
            FakeClient fake = FakeClient.Create(out IXrplClient client);
            XrplWallet[] wallets = { XrplWallet.Generate(), XrplWallet.Generate() };

            await client.SubmitMulti(UnsignedPayment(), wallets, autofill: false, failHard: failHard);

            Assert.IsNotNull(fake.Submitted);
            Assert.AreEqual(failHard, fake.Submitted.FailHard);
        }

        [TestMethod]
        public async Task TestUCheckAccountDeleteBlockers_ThrowsWhenTheAccountHasBlockers()
        {
            FakeClient fake = FakeClient.Create(out IXrplClient client);
            fake.Objects = new List<BaseLedgerEntry> { new LOCheck() };

            await Assert.ThrowsExactlyAsync<XrplException>(
                () => client.CheckAccountDeleteBlockers(new Dictionary<string, object> { ["Account"] = Account }));
        }

        [TestMethod]
        public async Task TestUCheckAccountDeleteBlockers_PassesWithoutBlockers()
        {
            FakeClient fake = FakeClient.Create(out IXrplClient client);
            fake.Objects = new List<BaseLedgerEntry>();

            await client.CheckAccountDeleteBlockers(new Dictionary<string, object> { ["Account"] = Account });
        }

        /// <summary>IXrplClient answering submit and account_objects; anything else fails the test.</summary>
        public class FakeClient : DispatchProxy
        {
            public SubmitRequest Submitted { get; private set; }

            public List<BaseLedgerEntry> Objects { get; set; } = new List<BaseLedgerEntry>();

            public static FakeClient Create(out IXrplClient client)
            {
                client = Create<IXrplClient, FakeClient>();
                return (FakeClient)(object)client;
            }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod?.Name)
                {
                    case nameof(IXrplClient.GRequest) when args[0] is SubmitRequest submit:
                        Submitted = submit;
                        return Task.FromResult(new XrplResponse<Submit>(new Submit(), default, null, null, null, null, false));
                    case nameof(IXrplClient.AccountObjects):
                        AccountObjects objects = new AccountObjects { AccountObjectList = Objects };
                        return Task.FromResult(new XrplResponse<AccountObjects>(objects, default, null, null, null, null, false));
                    default:
                        throw new NotSupportedException(targetMethod?.Name);
                }
            }
        }
    }
}
