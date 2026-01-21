using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUPermissionedDomainSet
    {
        private static List<Dictionary<string, dynamic>> CreateValidCredentials(int count = 1)
        {
            var credentials = new List<Dictionary<string, dynamic>>();
            for (int i = 0; i < count; i++)
            {
                credentials.Add(new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "6D795F63726564656E7469616C" }
                        }
                    }
                });
            }
            return credentials;
        }

        [TestMethod]
        public async Task TestVerify_Valid_CreateNewDomain()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", CreateValidCredentials(1) }
            };
            await Validation.ValidatePermissionedDomainSet(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Valid_ModifyExistingDomain()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 391u },
                { "DomainID", "77D6234D074E505024D39C04C3F262997B773719AB29ACFA83119E4210328776" },
                { "AcceptedCredentials", CreateValidCredentials(2) }
            };
            await Validation.ValidatePermissionedDomainSet(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Valid_MaxCredentials()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 392u },
                { "AcceptedCredentials", CreateValidCredentials(10) }
            };
            await Validation.ValidatePermissionedDomainSet(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Invalid_MissingAcceptedCredentials()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidatePermissionedDomainSet(tx),
                "PermissionedDomainSet: AcceptedCredentials is required");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_EmptyAcceptedCredentials()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", new List<Dictionary<string, dynamic>>() }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidatePermissionedDomainSet(tx),
                "PermissionedDomainSet: AcceptedCredentials must contain at least 1 credential");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_TooManyCredentials()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", CreateValidCredentials(11) }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidatePermissionedDomainSet(tx),
                "PermissionedDomainSet: AcceptedCredentials cannot contain more than 10 credentials");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_MissingCredentialIssuer()
        {
            var credentials = new List<Dictionary<string, dynamic>>
            {
                new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "CredentialType", "6D795F63726564656E7469616C" }
                        }
                    }
                }
            };
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", credentials }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidatePermissionedDomainSet(tx),
                "PermissionedDomainSet: Credential.Issuer is required");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_MissingCredentialType()
        {
            var credentials = new List<Dictionary<string, dynamic>>
            {
                new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" }
                        }
                    }
                }
            };
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", credentials }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidatePermissionedDomainSet(tx),
                "PermissionedDomainSet: Credential.CredentialType is required");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_CredentialTypeTooLong()
        {
            var longCredentialType = new string('A', 130);
            var credentials = new List<Dictionary<string, dynamic>>
            {
                new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", longCredentialType }
                        }
                    }
                }
            };
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", credentials }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidatePermissionedDomainSet(tx),
                "PermissionedDomainSet: Credential.CredentialType cannot exceed 64 bytes");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_DuplicateCredentials()
        {
            var credentials = new List<Dictionary<string, dynamic>>
            {
                new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "6D795F63726564656E7469616C" }
                        }
                    }
                },
                new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "6D795F63726564656E7469616C" }
                        }
                    }
                }
            };
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", credentials }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidatePermissionedDomainSet(tx),
                "PermissionedDomainSet: AcceptedCredentials cannot contain duplicate credentials");
        }

        [TestMethod]
        public async Task TestVerify_Valid_UniqueCredentialsSameIssuerDifferentType()
        {
            var credentials = new List<Dictionary<string, dynamic>>
            {
                new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "74797065315F63726564" }
                        }
                    }
                },
                new Dictionary<string, dynamic>
                {
                    { "Credential", new Dictionary<string, dynamic>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "74797065325F63726564" }
                        }
                    }
                }
            };
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "TransactionType", "PermissionedDomainSet" },
                { "Fee", "10" },
                { "Sequence", 390u },
                { "AcceptedCredentials", credentials }
            };
            await Validation.ValidatePermissionedDomainSet(tx);
            await Validation.Validate(tx);
        }
    }
}
