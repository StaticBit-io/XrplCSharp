using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;

using XrplFlags = Xrpl.Models.Utils.Flags;
using Common = Xrpl.Models.Transactions.Common;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Validator rules checked against rippled preflight and xrpl.js: STArray wrappers on Memos and
    /// Signers, numeric Flags from the dictionary layer, and the per-type rules for AMMDelete,
    /// Clawback, Payment and SignerListSet.
    /// </summary>
    [TestClass]
    public class TestUValidatorRules
    {
        private const string Account = "rMFMJsQaKEMEwwMRBHCoNXkFswJrWyNYLp";
        private const string Destination = "rJcvzBFCTcCrQdTLPZoRXJEboT9C3Wrd6H";
        private const string MptIssuanceId = "000004C463C52827307480341125DA0577DEFC38405B0E3E";

        private static Dictionary<string, object> Base(string type) => new Dictionary<string, object>
        {
            ["Account"] = Account,
            ["TransactionType"] = type,
        };

        private static Dictionary<string, object> Memo(Dictionary<string, object> fields)
            => new Dictionary<string, object> { ["Memo"] = fields };

        private static Dictionary<string, object> Signer() => new Dictionary<string, object>
        {
            ["Signer"] = new Dictionary<string, object>
            {
                ["Account"] = Destination,
                ["TxnSignature"] = "DEADBEEF",
                ["SigningPubKey"] = "0330E7FC9D56BB25D6893BA3F317AE5BCF33B3291BD63DB32654A313222F7FD020",
            },
        };

        [TestMethod]
        public void TestUIsMemo_WrappedMemoWithAnySubsetOfFields_IsValid()
        {
            Assert.IsTrue(Common.IsMemo(Memo(new Dictionary<string, object> { ["MemoData"] = "72656E74" })));
            Assert.IsTrue(Common.IsMemo(Memo(new Dictionary<string, object> { ["MemoType"] = "6869" })));
            Assert.IsTrue(Common.IsMemo(Memo(new Dictionary<string, object>
            {
                ["MemoType"] = "6869",
                ["MemoData"] = "72656E74",
                ["MemoFormat"] = "746578742F706C61696E",
            })));
        }

        [TestMethod]
        public void TestUIsMemo_UnwrappedNonHexOrUnknownField_IsInvalid()
        {
            Assert.IsFalse(Common.IsMemo(new Dictionary<string, object> { ["MemoData"] = "72656E74" }));
            Assert.IsFalse(Common.IsMemo(Memo(new Dictionary<string, object> { ["MemoData"] = "not hex" })));
            Assert.IsFalse(Common.IsMemo(Memo(new Dictionary<string, object> { ["MemoData"] = 12 })));
            Assert.IsFalse(Common.IsMemo(Memo(new Dictionary<string, object> { ["MemoText"] = "6869" })));
            Assert.IsFalse(Common.IsMemo(Memo(new Dictionary<string, object>())));
        }

        [TestMethod]
        public void TestUIsSigner_WrappedSignerIsValid_UnwrappedIsNot()
        {
            Assert.IsTrue(Common.IsSigner(Signer()));
            Assert.IsFalse(Common.IsSigner(Signer()["Signer"]));
        }

        [TestMethod]
        public void TestUValidate_BatchWithMemosAndSigners_Passes()
        {
            Batch batch = BatchUtils.Build(
                Account,
                new List<ITransactionRequest>
                {
                    new Payment { Account = Account, Destination = Destination, Amount = new Currency { Value = "1" }, Sequence = 2 },
                    new Payment { Account = Account, Destination = Destination, Amount = new Currency { Value = "1" }, Sequence = 3 },
                },
                BatchFlags.tfAllOrNothing);
            batch.Memos = new List<MemoWrapper> { new MemoWrapper { Memo = new Memo { MemoData = "72656E74", MemoType = "6869" } } };
            batch.Signers = new List<SignerWrapper>
            {
                new SignerWrapper
                {
                    Signer = new InnerSigner
                    {
                        Account = Destination,
                        TransactionSignature = "DEADBEEF",
                        SigningPublicKey = "0330E7FC9D56BB25D6893BA3F317AE5BCF33B3291BD63DB32654A313222F7FD020",
                    },
                },
            };
            Dictionary<string, object> tx = JsonSerializer.Deserialize<Dictionary<string, object>>(batch.ToJson(), XrplJsonOptions.Default)!;

            Validation.Validate(tx);

            Assert.AreEqual((uint)BatchFlags.tfAllOrNothing, tx["Flags"]);
        }

        [TestMethod]
        public void TestUSetTransactionFlagsToNumber_KeepsIntLongAndUlongFlags()
        {
            Dictionary<string, object> fromInt = Base("Batch");
            fromInt["Flags"] = 0x00010000;
            XrplFlags.SetTransactionFlagsToNumber(fromInt);
            Assert.AreEqual(0x00010000u, fromInt["Flags"]);

            Dictionary<string, object> fromLong = Base("Payment");
            fromLong["Flags"] = 2147483648L;
            XrplFlags.SetTransactionFlagsToNumber(fromLong);
            Assert.AreEqual(2147483648u, fromLong["Flags"]);

            Dictionary<string, object> fromUlong = Base("Payment");
            fromUlong["Flags"] = 131072UL;
            XrplFlags.SetTransactionFlagsToNumber(fromUlong);
            Assert.AreEqual(131072u, fromUlong["Flags"]);
        }

        [TestMethod]
        public void TestUSetTransactionFlagsToNumber_RejectsANegativeFlags()
        {
            Dictionary<string, object> tx = Base("Payment");
            tx["Flags"] = -1;
            Helper.ThrowsException<ValidationException>(() => XrplFlags.SetTransactionFlagsToNumber(tx));
        }

        [TestMethod]
        public void TestUConvertAccountSetFlags_UsesTfValues()
        {
            Dictionary<string, object> flags = new Dictionary<string, object>
            {
                ["tfRequireDestTag"] = true,
                ["tfDisallowXRP"] = true,
                ["tfAllowXRP"] = false,
            };

            Assert.AreEqual(0x00010000u | 0x00100000u, XrplFlags.ConvertAccountSetFlagsToNumber(flags));
            Helper.ThrowsException<ValidationException>(
                () => XrplFlags.ConvertAccountSetFlagsToNumber(new Dictionary<string, object> { ["asfRequireDest"] = true }));
        }

        [TestMethod]
        public void TestUValidatePayment_PartialPaymentFlagCombinedWithOthers_AllowsDeliverMin()
        {
            Dictionary<string, object> tx = Base("Payment");
            tx["Destination"] = Destination;
            tx["Amount"] = "1000";
            tx["DeliverMin"] = "500";

            tx["Flags"] = PaymentFlags.tfPartialPayment | PaymentFlags.tfNoDirectRipple;
            Validation.CheckPartialPayment(tx);

            tx["Flags"] = (int)(PaymentFlags.tfPartialPayment | PaymentFlags.tfLimitQuality);
            Validation.CheckPartialPayment(tx);

            tx["Flags"] = (int)PaymentFlags.tfNoDirectRipple;
            Helper.ThrowsException<ValidationException>(
                () => Validation.CheckPartialPayment(tx),
                "PaymentTransaction: tfPartialPayment flag required with DeliverMin");
        }

        [TestMethod]
        public void TestUValidateSignerListSet_QuorumZeroWithoutEntries_DeletesTheList()
        {
            Dictionary<string, object> tx = Base("SignerListSet");
            tx["SignerQuorum"] = 0;
            Validation.ValidateSignerListSet(tx);

            tx["SignerEntries"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["SignerEntry"] = new Dictionary<string, object> { ["Account"] = Destination, ["SignerWeight"] = 1 },
                },
            };
            Helper.ThrowsException<ValidationException>(
                () => Validation.ValidateSignerListSet(tx),
                "SignerListSet: SignerEntries must be omitted when SignerQuorum is 0");
        }

        [TestMethod]
        public void TestUValidateAMMDelete_AssetsAreIssues()
        {
            Dictionary<string, object> tx = Base("AMMDelete");
            tx["Asset"] = new Dictionary<string, object> { ["currency"] = "XRP" };
            tx["Asset2"] = new Dictionary<string, object> { ["currency"] = "USD", ["issuer"] = Destination };
            Validation.ValidateAMMDelete(tx);

            tx["Asset2"] = new Dictionary<string, object> { ["currency"] = "USD", ["issuer"] = Destination, ["value"] = "1" };
            Helper.ThrowsException<ValidationException>(
                () => Validation.ValidateAMMDelete(tx),
                "AMMDelete: Asset2 must be a Currency");

            tx["Asset2"] = "1000";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAMMDelete(tx));
        }

        [TestMethod]
        public void TestUValidateClawBack_MptAmountRequiresAHolderOtherThanTheIssuer()
        {
            Dictionary<string, object> tx = Base("Clawback");
            tx["Amount"] = new Dictionary<string, object> { ["value"] = "10", ["mpt_issuance_id"] = MptIssuanceId };

            Helper.ThrowsException<ValidationException>(() => Validation.ValidateClawBack(tx), "ClawBack: missing Holder");

            tx["Holder"] = Account;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateClawBack(tx), "ClawBack: invalid holder Account");

            tx["Holder"] = Destination;
            Validation.ValidateClawBack(tx);
        }

        [TestMethod]
        public void TestUValidateClawBack_IssuedAmountRejectsHolder()
        {
            Dictionary<string, object> tx = Base("Clawback");
            tx["Amount"] = new Dictionary<string, object> { ["currency"] = "USD", ["issuer"] = Destination, ["value"] = "10" };
            Validation.ValidateClawBack(tx);

            tx["Holder"] = Destination;
            Helper.ThrowsException<ValidationException>(
                () => Validation.ValidateClawBack(tx),
                "ClawBack: cannot have Holder for currency");
        }
    }
}
