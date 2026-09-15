

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/offerCreate.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUOfferCancel
    {
        public static Dictionary<string, object> offer;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            offer = new Dictionary<string, object>
            {
                {"TransactionType", "OfferCancel"},
                {"Account", "rnKiczmiQkZFiDES8THYyLA2pQohC5C6EF"},
                {"Fee", "10"},
                {"LastLedgerSequence", 65477334u},
                {"OfferSequence", 60797528u},
                {"Sequence", 60797535u},
                {"Flags", 2147483648u},
                {"SigningPubKey", "0369C9BC4D18FAE741898828A1F48E53E53F6F3DB3191441CC85A14D4FC140E031"},
                {"TxnSignature", "304402203EC848BD6AB42DC8509285245804B15E1652092CC0B189D369E12E563771D049022046DF40C16EA05DC99D01E553EA2E218FCA1C5B38927889A2BDF064D1F44D60F0"},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {

            //verifies valid OfferCancel
            Validation.ValidateOfferCancel(offer);
            Validation.Validate(offer);

            // verifies valid OfferCancel with flags
            offer["Flags"] = 2147483648;
            Validation.ValidateOfferCancel(offer);
            Validation.Validate(offer);

            // throws w/ OfferSequence must be a number
            offer["OfferSequence"] = "99";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateOfferCancel(offer), "OfferCancel: OfferSequence must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(offer), "OfferCancel: OfferSequence must be a number");
            offer["OfferSequence"] = 60797528u;

            // throws w/ missing OfferSequence
            offer.Remove("OfferSequence");
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateOfferCancel(offer), "OfferCancel: missing field OfferSequence");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(offer), "OfferCancel: missing field OfferSequence");
            offer["OfferSequence"] = 60797528u;

        }
    }

}

