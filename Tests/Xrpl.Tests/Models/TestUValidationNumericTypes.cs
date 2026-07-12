using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

using TxCommon = Xrpl.Models.Transactions.Common;
using Xrpl.Wallet;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// DictionaryObjectConverter materializes JSON numbers as int (then long/ulong),
    /// while validation historically type-checked for uint only — rejecting valid
    /// transactions produced via TransactionRequest.ToDictionary().
    /// These tests pin the accepted integral representations.
    /// </summary>
    [TestClass]
    public class TestUValidationNumericTypes
    {
        private static string Account1;
        private static string Account2;

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            Account1 = XrplWallet.Generate().ClassicAddress;
            Account2 = XrplWallet.Generate().ClassicAddress;
        }

        [TestMethod]
        public async Task TestUValidateBaseTransaction_FromToDictionary_Passes()
        {
            Payment payment = new Payment
            {
                Account = Account1,
                Destination = Account2,
                Amount = new Currency { ValueAsXrp = 1m },
                Sequence = 5,
                SourceTag = 42,
                LastLedgerSequence = 1000000,
                Fee = new Currency { Value = "12" },
            };

            // ToDictionary goes through DictionaryObjectConverter: numbers arrive as int, not uint
            Dictionary<string, object> tx = payment.ToDictionary();
            Assert.IsInstanceOfType<int>(tx["Sequence"], "Precondition: the converter materializes small numbers as int.");

            await TxCommon.ValidateBaseTransaction(tx);
        }

        [TestMethod]
        public async Task TestUValidateBaseTransaction_NegativeAndOutOfRange_Throw()
        {
            Dictionary<string, object> tx = new()
            {
                ["Account"] = Account1,
                ["TransactionType"] = "Payment",
                ["SourceTag"] = -1,
            };
            await Assert.ThrowsExactlyAsync<ValidationException>(() => TxCommon.ValidateBaseTransaction(tx));

            tx.Remove("SourceTag");
            tx["Sequence"] = (long)uint.MaxValue + 1;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => TxCommon.ValidateBaseTransaction(tx));

            tx["Sequence"] = "5";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => TxCommon.ValidateBaseTransaction(tx));
        }

        [TestMethod]
        public async Task TestUValidateAccountSet_SetFlagAsInt_NoInvalidCast()
        {
            AccountSet accountSet = new AccountSet
            {
                Account = Account1,
                SetFlag = AccountSetAsfFlags.asfRequireDest,
                Sequence = 1,
                Fee = new Currency { Value = "12" },
            };
            // Pre-fix this path threw InvalidCastException from the (uint)SetFlag unbox on a boxed int
            await Validation.ValidateAccountSet(accountSet.ToDictionary());
        }

        [TestMethod]
        public async Task TestUValidateTicketCreate_CountAsInt_Passes()
        {
            TicketCreate ticketCreate = new TicketCreate
            {
                Account = Account1,
                TicketCount = 5,
                Sequence = 1,
                Fee = new Currency { Value = "12" },
            };
            await Validation.ValidateTicketCreate(ticketCreate.ToDictionary());
        }

        [TestMethod]
        public async Task TestUValidateEscrowCreate_WithoutDestinationTag_Passes()
        {
            // Pre-fix the guard tested the required Destination instead of the optional
            // DestinationTag, so every escrow without a tag failed validation
            Dictionary<string, object> tx = new()
            {
                ["TransactionType"] = "EscrowCreate",
                ["Account"] = Account1,
                ["Destination"] = Account2,
                ["Amount"] = "1000000",
                ["FinishAfter"] = 800000000u,
            };
            await Validation.ValidateEscrowCreate(tx);

            tx["DestinationTag"] = "not-a-number";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => Validation.ValidateEscrowCreate(tx));
        }

        [TestMethod]
        public void TestUIntegralHelpers()
        {
            Assert.IsTrue(TxCommon.IsUInt32(5u));
            Assert.IsTrue(TxCommon.IsUInt32(5));
            Assert.IsTrue(TxCommon.IsUInt32(5L));
            Assert.IsTrue(TxCommon.IsUInt32((ulong)5));
            Assert.IsTrue(TxCommon.IsUInt32((long)uint.MaxValue));
            Assert.IsFalse(TxCommon.IsUInt32(-1));
            Assert.IsFalse(TxCommon.IsUInt32((long)uint.MaxValue + 1));
            Assert.IsFalse(TxCommon.IsUInt32((ulong)uint.MaxValue + 1));
            Assert.IsFalse(TxCommon.IsUInt32("5"));
            Assert.IsFalse(TxCommon.IsUInt32(null));

            Assert.IsTrue(TxCommon.TryGetUInt32(1073741824, out uint v) && v == 0x40000000u);
            Assert.IsFalse(TxCommon.TryGetUInt32(-1, out _));
        }
    }
}
