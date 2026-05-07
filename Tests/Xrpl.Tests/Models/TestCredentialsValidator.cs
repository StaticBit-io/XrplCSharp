// Unit tests for the shared CredentialsValidator helper.

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCredentialsValidator
    {
        private const string TxType = "TestTx";
        private const string Field = "CredentialIDs";

        private const string ValidId1 = "A1B2C3D4E5F6789012345678901234567890ABCDEF1234567890ABCDEF123456";
        private const string ValidId2 = "1111222233334444555566667777888899990000AAAABBBBCCCCDDDDEEEE0001";

        [TestMethod]
        public void NullList_NoOp()
        {
            CredentialsValidator.ValidateCredentialsList(null, TxType, Field, isStringID: true);
        }

        [TestMethod]
        public async Task EmptyList_Throws()
        {
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(new List<object>(), TxType, Field, isStringID: true)),
                $"{TxType}: {Field} cannot be empty");
        }

        [TestMethod]
        public async Task TooManyItems_Throws()
        {
            List<object> ids = new List<object>();
            for (int i = 0; i < 9; i++)
            {
                ids.Add(ValidId1.Substring(0, 60) + i.ToString("X4"));
            }

            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(ids, TxType, Field, isStringID: true)),
                $"{TxType}: {Field} cannot contain more than 8 elements");
        }

        [TestMethod]
        public async Task NonHex_Throws()
        {
            List<object> ids = new List<object> { new string('Z', 64) };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(ids, TxType, Field, isStringID: true)),
                $"{TxType}: {Field}[0] must be a 64-character hexadecimal object ID");
        }

        [TestMethod]
        public async Task WrongLength_Throws()
        {
            List<object> ids = new List<object> { "ABC123" };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(ids, TxType, Field, isStringID: true)),
                $"{TxType}: {Field}[0] must be a 64-character hexadecimal object ID");
        }

        [TestMethod]
        public async Task DuplicateIds_Throws()
        {
            List<object> ids = new List<object> { ValidId1, ValidId1.ToLowerInvariant() };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(ids, TxType, Field, isStringID: true)),
                $"{TxType}: {Field} cannot contain duplicate credential IDs");
        }

        [TestMethod]
        public void ValidIds_Pass()
        {
            List<object> ids = new List<object> { ValidId1, ValidId2 };
            CredentialsValidator.ValidateCredentialsList(ids, TxType, Field, isStringID: true);
        }

        [TestMethod]
        public void ValidObjects_Pass()
        {
            List<object> objs = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "Credential", new Dictionary<string, object>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "6D795F63726564656E7469616C" }
                        }
                    }
                }
            };
            CredentialsValidator.ValidateCredentialsList(objs, TxType, "AuthorizeCredentials", isStringID: false);
        }

        [TestMethod]
        public async Task ObjectMissingCredential_Throws()
        {
            List<object> objs = new List<object>
            {
                new Dictionary<string, object>()
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(objs, TxType, "AuthorizeCredentials", isStringID: false)),
                $"{TxType}: AuthorizeCredentials[0] must be an object with a Credential field");
        }

        [TestMethod]
        public async Task ObjectMissingIssuer_Throws()
        {
            List<object> objs = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "Credential", new Dictionary<string, object>
                        {
                            { "CredentialType", "ABCD" }
                        }
                    }
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(objs, TxType, "AuthorizeCredentials", isStringID: false)),
                $"{TxType}: AuthorizeCredentials[0].Credential.Issuer is required and must be a string");
        }

        [TestMethod]
        public async Task ObjectNonHexCredentialType_Throws()
        {
            List<object> objs = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "Credential", new Dictionary<string, object>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "ZZZZ" }
                        }
                    }
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(objs, TxType, "AuthorizeCredentials", isStringID: false)),
                $"{TxType}: AuthorizeCredentials[0].Credential.CredentialType must be a hexadecimal string");
        }

        [TestMethod]
        public async Task ObjectDuplicateCredentials_Throws()
        {
            Dictionary<string, object> cred = new Dictionary<string, object>
            {
                { "Credential", new Dictionary<string, object>
                    {
                        { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                        { "CredentialType", "ABCD" }
                    }
                }
            };
            List<object> objs = new List<object> { cred, cred };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Task.Run(() => CredentialsValidator.ValidateCredentialsList(objs, TxType, "AuthorizeCredentials", isStringID: false)),
                $"{TxType}: AuthorizeCredentials cannot contain duplicate credentials");
        }
    }
}
