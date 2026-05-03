

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/depositPreauth.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUDepositPreauth
    {
        public static Dictionary<string, object> depositPreauth;

        private const string MissingError =
            "DepositPreauth: must provide one of Authorize, Unauthorize, AuthorizeCredentials or UnauthorizeCredentials";

        private const string ExclusiveError =
            "DepositPreauth: exactly one of Authorize, Unauthorize, AuthorizeCredentials or UnauthorizeCredentials must be provided";

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            depositPreauth = new Dictionary<string, object>
            {
                {"TransactionType", "DepositPreauth"},
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
            };
        }

        private static List<Dictionary<string, object>> CreateValidCredentials(int count = 1)
        {
            List<Dictionary<string, object>> credentials = new List<Dictionary<string, object>>();
            for (int i = 0; i < count; i++)
            {
                credentials.Add(new Dictionary<string, object>
                {
                    { "Credential", new Dictionary<string, object>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", $"6D795F63726564656E7469616C{i:D2}" }
                        }
                    }
                });
            }

            return credentials;
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {
            depositPreauth.Remove("Authorize");
            depositPreauth.Remove("Unauthorize");
            depositPreauth.Remove("AuthorizeCredentials");
            depositPreauth.Remove("UnauthorizeCredentials");

            depositPreauth["Authorize"] = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";
            await Validation.ValidateDepositPreauth(depositPreauth);
            await Validation.Validate(depositPreauth);
            depositPreauth.Remove("Authorize");

            depositPreauth["Unauthorize"] = "raKEEVSGnKSD9Zyvxu4z6Pqpm4ABH8FS6n";
            await Validation.ValidateDepositPreauth(depositPreauth);
            await Validation.Validate(depositPreauth);
            depositPreauth.Remove("Unauthorize");

            depositPreauth["Unauthorize"] = "raKEEVSGnKSD9Zyvxu4z6Pqpm4ABH8FS6n";
            depositPreauth["Authorize"] = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateDepositPreauth(depositPreauth), ExclusiveError);
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(depositPreauth), ExclusiveError);
            depositPreauth.Remove("Authorize");
            depositPreauth.Remove("Unauthorize");

            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateDepositPreauth(depositPreauth), MissingError);
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(depositPreauth), MissingError);

            depositPreauth["Authorize"] = 1234;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateDepositPreauth(depositPreauth), "DepositPreauth: Authorize must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(depositPreauth), "DepositPreauth: Authorize must be a string");
            depositPreauth.Remove("Authorize");

            depositPreauth["Unauthorize"] = 1234;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateDepositPreauth(depositPreauth), "DepositPreauth: Unauthorize must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(depositPreauth), "DepositPreauth: Unauthorize must be a string");
            depositPreauth.Remove("Unauthorize");

            depositPreauth["Unauthorize"] = depositPreauth["Account"];
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateDepositPreauth(depositPreauth), "DepositPreauth: Account can't unauthorize its own address");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(depositPreauth), "DepositPreauth: Account can't unauthorize its own address");
            depositPreauth.Remove("Unauthorize");
        }

        [TestMethod]
        public async Task TestVerifyValid_AuthorizeCredentials()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "DepositPreauth" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "AuthorizeCredentials", CreateValidCredentials(3) }
            };
            await Validation.ValidateDepositPreauth(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerifyValid_UnauthorizeCredentials()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "DepositPreauth" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "UnauthorizeCredentials", CreateValidCredentials(8) }
            };
            await Validation.ValidateDepositPreauth(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Invalid_TooManyCredentials()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "DepositPreauth" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "AuthorizeCredentials", CreateValidCredentials(9) }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDepositPreauth(tx),
                "DepositPreauth: AuthorizeCredentials cannot contain more than 8 elements");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_DuplicateAuthorizeCredentials()
        {
            List<Dictionary<string, object>> credentials = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "Credential", new Dictionary<string, object>
                        {
                            { "Issuer", "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX" },
                            { "CredentialType", "6D795F63726564656E7469616C" }
                        }
                    }
                },
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

            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "DepositPreauth" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "AuthorizeCredentials", credentials }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDepositPreauth(tx),
                "DepositPreauth: AuthorizeCredentials cannot contain duplicate credentials");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_BothAuthorizeAndAuthorizeCredentials()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "DepositPreauth" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "Authorize", "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW" },
                { "AuthorizeCredentials", CreateValidCredentials(1) }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDepositPreauth(tx),
                ExclusiveError);
        }

        [TestMethod]
        public async Task TestVerify_Invalid_BothAuthorizeAndUnauthorizeCredentials()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "DepositPreauth" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "AuthorizeCredentials", CreateValidCredentials(1) },
                { "UnauthorizeCredentials", CreateValidCredentials(1) }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDepositPreauth(tx),
                ExclusiveError);
        }

        [TestMethod]
        public async Task TestVerify_Invalid_EmptyCredentialsList()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "DepositPreauth" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "AuthorizeCredentials", new List<Dictionary<string, object>>() }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDepositPreauth(tx),
                "DepositPreauth: AuthorizeCredentials cannot be empty");
        }
    }
}
