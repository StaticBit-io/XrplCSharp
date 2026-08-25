using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

namespace Xrpl.Tests.Models
{
    /// <summary>
    /// Places where the model told the consumer something that was not so.
    /// </summary>
    /// <remarks>
    /// Four separate reports, one theme: a type or a validator that answers confidently and wrongly.
    /// None of them fails loudly - an inverted predicate returns a bool, a field that does not exist
    /// in the protocol serializes fine, a pattern match on the wrong half of a type pair compiles
    /// and finds nothing. The cost lands on the consumer, at a node refusal or, worse, at a
    /// transaction that succeeded meaning something else.
    /// </remarks>
    [TestClass]
    public class TestUModelTruth
    {
        /// <summary>
        /// <c>IsMPTToken</c> answered the exact opposite: issue #128.
        /// </summary>
        /// <remarks>
        /// The negation was missing, so every amount that is not a multi-purpose token was reported
        /// as one. Nothing inside the SDK calls this method, which is why nothing showed it.
        /// </remarks>
        [TestMethod]
        public void TestUIsMPTTokenIsTrueForAnMPTAndFalseForEverythingElse()
        {
            Currency mpt = new Currency
            {
                MPTokenIssuanceID = "00000539C0B4D5EB1B4A8B0A5C2E0C8C6A0F6D1E2B3C4D5E",
                Value = "10",
            };
            Currency xrp = new Currency { ValueAsXrp = 5 };
            Currency issued = new Currency
            {
                CurrencyCode = "USD",
                Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                Value = "10",
            };

            Assert.IsTrue(mpt.IsMPTToken(), "An amount carrying an issuance id is the only kind this can be true of.");
            Assert.IsFalse(xrp.IsMPTToken(), "XRP is not a multi-purpose token.");
            Assert.IsFalse(issued.IsMPTToken(), "An issued currency is not a multi-purpose token.");
            Assert.IsFalse(((Currency)null).IsMPTToken(), "Nothing at all is not a multi-purpose token either.");
        }

        /// <summary>
        /// <c>NFTokenAcceptOffer</c> no longer offers a field the protocol does not have: issue #129.
        /// </summary>
        /// <remarks>
        /// rippled's <c>transactions.macro</c> gives this transaction exactly three of its own
        /// fields - <c>NFTokenBuyOffer</c>, <c>NFTokenSellOffer</c>, <c>NFTokenBrokerFee</c>. The
        /// model declared a fourth, <c>NFTokenID</c>, and it serialized like any other: the type
        /// suggested it, IntelliSense offered it, and whoever filled it in got a node refusal with
        /// no hint from the types at all.
        /// <para>
        /// Asserted through the property list rather than only through serialization, because the
        /// serialized form hides an unset property and the point is that the property is gone.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void TestUNFTokenAcceptOfferHasNoNFTokenID()
        {
            foreach (Type type in new[]
                     {
                         typeof(NFTokenAcceptOffer),
                         typeof(NFTokenAcceptOfferResponse),
                         typeof(INFTokenAcceptOffer),
                     })
            {
                Assert.IsNull(
                    type.GetProperty("NFTokenID"),
                    $"{type.Name} still declares NFTokenID, which NFTokenAcceptOffer does not have in the protocol.");
            }
        }

        /// <summary>
        /// And what it does have still serializes, so the removal did not take a real field with it.
        /// </summary>
        [TestMethod]
        public void TestUNFTokenAcceptOfferStillCarriesItsOwnThreeFields()
        {
            NFTokenAcceptOffer accept = new NFTokenAcceptOffer
            {
                Account = "r4f4xLpXJtCh9PwdzsQ6KYwLevVnBpJV6f",
                NFTokenSellOffer = "392578EC763875C71944D25F07528F28D5460A6DD2958A17792380D9E2B430A7",
                NFTokenBuyOffer = "68CD1F6F906494EA08C9CB5CAFA64DFA90D4E834B7151899B73231DE5A0C3B77",
            };

            string json = JsonSerializer.Serialize(accept, XrplJsonOptions.Default);

            StringAssert.Contains(json, "NFTokenSellOffer");
            StringAssert.Contains(json, "NFTokenBuyOffer");
            Assert.IsFalse(json.Contains("NFTokenID"), $"NFTokenID must not reach the wire: {json}");
        }

        /// <summary>
        /// The same offer on both sides is a refusal the node charges for, and one comparison
        /// catches it: issue #134.
        /// </summary>
        /// <remarks>
        /// rippled compares each offer's owner with the submitter separately - the two blocks in
        /// <c>preclaim</c> are not alternatives - so naming one offer twice makes one of those
        /// comparisons the account against itself, and the answer is
        /// <c>tecCANT_ACCEPT_OWN_NFTOKEN_OFFER</c>: in a ledger, fee taken.
        /// </remarks>
        [TestMethod]
        public async System.Threading.Tasks.Task TestUTheSameOfferOnBothSidesIsRefused()
        {
            const string offer = "392578EC763875C71944D25F07528F28D5460A6DD2958A17792380D9E2B430A7";
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "NFTokenAcceptOffer" },
                { "Account", "r4f4xLpXJtCh9PwdzsQ6KYwLevVnBpJV6f" },
                { "NFTokenSellOffer", offer },
                { "NFTokenBuyOffer", offer },
            };

            ValidationException error = await Assert.ThrowsExactlyAsync<ValidationException>(
                () => Validation.ValidateNFTokenAcceptOffer(tx));

            StringAssert.Contains(error.Message, "different offers");
        }

        /// <summary>
        /// Two different offers are brokered mode, which is legal and must stay so.
        /// </summary>
        /// <remarks>
        /// Without this, a check that refused every brokered transaction would satisfy the test above.
        /// </remarks>
        [TestMethod]
        public async System.Threading.Tasks.Task TestUTwoDifferentOffersAreAccepted()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "NFTokenAcceptOffer" },
                { "Account", "r4f4xLpXJtCh9PwdzsQ6KYwLevVnBpJV6f" },
                { "NFTokenSellOffer", "392578EC763875C71944D25F07528F28D5460A6DD2958A17792380D9E2B430A7" },
                { "NFTokenBuyOffer", "68CD1F6F906494EA08C9CB5CAFA64DFA90D4E834B7151899B73231DE5A0C3B77" },
            };

            await Validation.ValidateNFTokenAcceptOffer(tx);
        }

        /// <summary>
        /// Transactions read back from the ledger can be matched on the <c>I</c> interface they
        /// share with their request type: issue #135.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The advice this test guards is in the README and on
        /// <c>TransactionSummary.Transaction</c>: match on <c>INFTokenCreateOffer</c>, never on
        /// <c>NFTokenCreateOffer</c>, because what arrives is the response half. Advice like that
        /// is only worth giving while it holds for the types it is given about.
        /// </para>
        /// <para>
        /// Five pairs do not hold it, and they are listed rather than hidden: the
        /// <c>ConfidentialMPT</c> set carries no interface at all - neither half declares one - so
        /// for those there is nothing to match on. The list is the point of the test as much as the
        /// invariant is: a sixth such type added tomorrow fails here rather than being discovered
        /// by a consumer whose pattern match silently found nothing.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void TestURequestAndResponseHalvesShareAnInterface()
        {
            HashSet<string> knownWithoutAnInterface = new HashSet<string>(StringComparer.Ordinal)
            {
                "ConfidentialMPTConvert",
                "ConfidentialMPTConvertBack",
                "ConfidentialMPTMergeInbox",
                "ConfidentialMPTSend",
                "ConfidentialMPTClawback",
            };

            Assembly assembly = typeof(TransactionRequest).Assembly;
            List<string> missing = new List<string>();
            int pairs = 0;

            foreach (Type response in assembly.GetTypes()
                         .Where(type => type.IsClass && !type.IsAbstract)
                         .Where(type => type.Name.EndsWith("Response", StringComparison.Ordinal))
                         .Where(typeof(TransactionResponse).IsAssignableFrom))
            {
                string requestName = response.Name.Substring(0, response.Name.Length - "Response".Length);
                Type request = assembly.GetType($"{response.Namespace}.{requestName}");
                if (request is null || knownWithoutAnInterface.Contains(requestName))
                {
                    continue;
                }

                pairs++;
                bool shared = request.GetInterfaces()
                    .Intersect(response.GetInterfaces())
                    .Any(contract => contract.Name.StartsWith("I" + requestName, StringComparison.Ordinal));

                if (!shared)
                {
                    missing.Add(requestName);
                }
            }

            Assert.IsTrue(pairs > 50, $"The scan must actually have found the model types; it saw {pairs} pairs.");
            Assert.AreEqual(
                0,
                missing.Count,
                $"These request/response pairs share no I-interface, so history cannot be matched on " +
                $"one and the advice in the README does not hold for them: {string.Join(", ", missing)}");
        }
    }
}
