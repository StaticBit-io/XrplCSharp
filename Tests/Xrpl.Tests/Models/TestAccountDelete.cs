

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/accountDelete.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUAccountDelete
    {
        [TestMethod]
        public async Task TestVerify_Valid_AccountDelete()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountDelete" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Destination", "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe"},
                {"DestinationTag", 13u},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", 2147483648u},
            };
            await Validation.ValidateAccountDelete(tx);
        }
        [TestMethod]
        public async Task TestVerify_InValid_missing_Destination()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountDelete" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", 2147483648u},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountDelete(tx), "AccountDelete: missing field Destination");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "AccountDelete: missing field Destination");
        }
        [TestMethod]
        public async Task TestVerify_Invalid_Destination()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountDelete" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Destination", 65478965},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", 2147483648u},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountDelete(tx), "AccountDelete: invalid Destination");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "AccountDelete: invalid Destination");
        }
        [TestMethod]
        public async Task TestVerify_Invalid_DestinationTag()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountDelete" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Destination", "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe"},
                {"DestinationTag", "sdfsdfdsf"},
                {"Fee", "5000000"},
                {"Sequence", 2470665u},
                { "Flags", 2147483648u},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountDelete(tx), "AccountDelete: invalid DestinationTag");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "AccountDelete: invalid DestinationTag");
        }

        [TestMethod]
        public async Task TestVerify_Valid_AccountDelete_WithCredentialIDs()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountDelete" },
                { "Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm" },
                { "Destination", "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe" },
                { "Fee", "5000000" },
                { "Sequence", 2470665u },
                { "CredentialIDs", new List<object> { "A1B2C3D4E5F6789012345678901234567890ABCDEF1234567890ABCDEF123456" } }
            };
            await Validation.ValidateAccountDelete(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Invalid_AccountDelete_CredentialIDsTooMany()
        {
            List<object> ids = new List<object>();
            for (int i = 0; i < 9; i++)
            {
                ids.Add($"A1B2C3D4E5F6789012345678901234567890ABCDEF1234567890ABCDEF1234{i:X2}");
            }

            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountDelete" },
                { "Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm" },
                { "Destination", "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe" },
                { "Fee", "5000000" },
                { "Sequence", 2470665u },
                { "CredentialIDs", ids }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateAccountDelete(tx),
                "AccountDelete: CredentialIDs cannot contain more than 8 elements");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_AccountDelete_CredentialIDsNonHex()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountDelete" },
                { "Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm" },
                { "Destination", "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe" },
                { "Fee", "5000000" },
                { "Sequence", 2470665u },
                { "CredentialIDs", new List<object> { new string('Z', 64) } }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateAccountDelete(tx),
                "AccountDelete: CredentialIDs[0] must be a 64-character hexadecimal object ID");
        }
    }
}

