using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;

using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;
using Xrpl.Utils;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/NFTokenMint.ts

namespace XrplTests.Xrpl.Models
{

    /// <summary>
    /// NFTokenMint Transaction Verification Testing.<br/>
    /// Providing runtime verification testing for each specific transaction type.
    /// </summary>
    [TestClass]
    public class TestUNFTokenMint
    {
        [TestMethod]
        public void TestVerify_Valid_NFTokenMint()
        {
            var offer = new Dictionary<string, object>
            {
                { "TransactionType", "NFTokenMint" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", NFTokenMintFlags.tfTransferable },
                {"NFTokenTaxon", 0},
                {"Issuer", "r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ"},
                {"TransferFee", 1},
                {"URI", "http://xrpl.org".ConvertStringToHex()},
            };
            Validation.Validate(offer);
        }
        [TestMethod]
        public void TestVerify_InValid_missing_NFTokenTaxon()
        {
            var offer = new Dictionary<string, object>
            {
                { "TransactionType", "NFTokenMint" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", NFTokenMintFlags.tfTransferable },
                {"Issuer", "r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ"},
                {"TransferFee", 1},
                {"URI", "http://xrpl.org".ConvertStringToHex()},
            };
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(offer), "NFTokenMint: missing field NFTokenTaxon");
        }
        [TestMethod]
        public void TestVerify_Invalid_Account_is_Issuer()
        {
            var offer = new Dictionary<string, object>
            {
                { "TransactionType", "NFTokenMint" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", NFTokenMintFlags.tfTransferable },
                {"NFTokenTaxon", 0},
                {"Issuer", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"TransferFee", 1},
                {"URI", "http://xrpl.org".ConvertStringToHex()},
            };
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(offer), "NFTokenMint: Issuer must not be equal to Account");
        }
        [TestMethod]
        public void TestVerify_Invalid_URI_not_in_hex_format()
        {
            var offer = new Dictionary<string, object>
            {
                { "TransactionType", "NFTokenMint" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", NFTokenMintFlags.tfTransferable },
                {"NFTokenTaxon", 0},
                {"Issuer", "r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ"},
                {"TransferFee", 1},
                {"URI", "http://xrpl.org"},
            };
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(offer), "NFTokenMint: URI must be in hex format");
        }
    }

}
