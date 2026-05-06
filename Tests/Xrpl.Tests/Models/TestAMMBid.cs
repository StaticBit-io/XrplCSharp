

// https://github.com/XRPLF/xrpl.js/blob/amm-beta/packages/xrpl/test/models/AMMBid.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUAMMBid
    {
        public static Dictionary<string, object> bid;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            bid = new Dictionary<string, object>
            {
                {"TransactionType", "AMMBid"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Asset", new Dictionary<string,object>(){{"currency","XRP"}}},
                {"Asset2", new Dictionary<string,object>(){{"currency","ETH"},{"issuer", "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd" } }},
                {"BidMin", "5"},
                {"BidMax", "10"},
                {"AuthAccounts", new List<Dictionary<string,object>>() {
                    new Dictionary<string,object>()
                    {
                        {"AuthAccount",new Dictionary<string,object>()
                        {
                            { "Account", "rNZdsTBP5tH1M6GHC6bTreHAp6ouP8iZSh" }
                        }}
                    },
                    new Dictionary<string,object>()
                    {
                        {"AuthAccount",new Dictionary<string,object>()
                        {
                            { "Account", "rfpFv97Dwu89FTyUwPjtpZBbuZxTqqgTmH" }
                        }}
                    },
                    new Dictionary<string,object>()
                    {
                        {"AuthAccount",new Dictionary<string,object>()
                        {
                            { "Account", "rzzYHPGb8Pa64oqxCzmuffm122bitq3Vb" }
                        }}
                    },
                    new Dictionary<string,object>()
                    {
                        {"AuthAccount",new Dictionary<string,object>()
                        {
                            { "Account", "rhwxHxaHok86fe4LykBom1jSJ3RYQJs1h4" }
                        }}
                    }, }
                },
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {
            //verifies valid AMMBid
            await Validation.Validate(bid);

            //throws w/ missing field Asset
            bid.Remove("Asset");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: missing field Asset");
            bid["Asset"] = new Dictionary<string, object>() { { "currency", "XRP" } };
            //throws w/ Asset must be an Issue
            bid["Asset"] = 1234;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: Asset must be an Issue");
            bid["Asset"] = new Dictionary<string, object>() { { "currency", "XRP" } };
            //throws w/ missing field Asset2
            bid.Remove("Asset2");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: missing field Asset2");
            bid["Asset2"] = new Dictionary<string, object>() { { "currency", "ETH" }, { "issuer", "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd" } };
            //throws w/ Asset2 must be an Issue
            bid["Asset2"] = 1234;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: Asset2 must be an Issue");
            bid["Asset2"] = new Dictionary<string, object>() { { "currency", "ETH" }, { "issuer", "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd" } };

            //throws w/ BidMin must be an Amount
            bid["BidMin"] = 5;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: BidMin must be an Amount");
            bid["BidMin"] = "5";

            //throws w/ BidMax must be an Amount
            bid["BidMax"] = 10;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: BidMax must be an Amount");
            bid["BidMax"] = "10";

            //throws w/ AuthAccounts length must not be greater than 4
            bid["AuthAccounts"] = new List<Dictionary<string, object>>()
            {
                new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rNZdsTBP5tH1M6GHC6bTreHAp6ouP8iZSh" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rfpFv97Dwu89FTyUwPjtpZBbuZxTqqgTmH" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rzzYHPGb8Pa64oqxCzmuffm122bitq3Vb" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rhwxHxaHok86fe4LykBom1jSJ3RYQJs1h4" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "r3X6noRsvaLapAKCG78zAtWcbhB3sggS1s" }
                    }}
                },
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: AuthAccounts length must not be greater than 4");

            //throws w/ AuthAccounts must be an AuthAccount array
            bid["AuthAccounts"] = 1234;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: AuthAccounts must be an AuthAccount array");

            bid["AuthAccounts"] = new List<Dictionary<string, object>>()
            {
                new Dictionary<string,object>()
                {
                    {"AuthAccount",null}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rfpFv97Dwu89FTyUwPjtpZBbuZxTqqgTmH" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rzzYHPGb8Pa64oqxCzmuffm122bitq3Vb" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rhwxHxaHok86fe4LykBom1jSJ3RYQJs1h4" }
                    }}
                }
            };

            //throws w/ invalid AuthAccounts when AuthAccount is undefined
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: invalid AuthAccounts");
            //throws w/ invalid AuthAccounts when AuthAccount is not an object
            bid["AuthAccounts"] = new List<Dictionary<string, object>>()
            {
                new Dictionary<string,object>()
                {
                    {"AuthAccount",1234}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rfpFv97Dwu89FTyUwPjtpZBbuZxTqqgTmH" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rzzYHPGb8Pa64oqxCzmuffm122bitq3Vb" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rhwxHxaHok86fe4LykBom1jSJ3RYQJs1h4" }
                    }}
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: invalid AuthAccounts");
            // throws w/ invalid AuthAccounts when AuthAccount.Account is not a string
            bid["AuthAccounts"] = new List<Dictionary<string, object>>()
            {
                new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", 1234 }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rfpFv97Dwu89FTyUwPjtpZBbuZxTqqgTmH" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rzzYHPGb8Pa64oqxCzmuffm122bitq3Vb" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rhwxHxaHok86fe4LykBom1jSJ3RYQJs1h4" }
                    }}
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: invalid AuthAccounts");
            //throws w/ AuthAccounts must not include sender's address
            bid["AuthAccounts"] = new List<Dictionary<string, object>>()
            {
                new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", bid["Account"] }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rfpFv97Dwu89FTyUwPjtpZBbuZxTqqgTmH" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rzzYHPGb8Pa64oqxCzmuffm122bitq3Vb" }
                    }}
                }, new Dictionary<string,object>()
                {
                    {"AuthAccount",new Dictionary<string,object>()
                    {
                        { "Account", "rhwxHxaHok86fe4LykBom1jSJ3RYQJs1h4" }
                    }}
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(bid), "AMMBid: AuthAccounts must not include sender's address");

        }
    }
}

