using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Enums;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// The path step `type` field is a bitmask (STPathElement in rippled) and is modelled as the
    /// [Flags] enum Xrpl.Models.Enums.PathStepType. It must stay a number on the wire:
    /// XrplJsonOptions deliberately registers no global JsonStringEnumConverter, because XRPL
    /// protocol enums are numeric.
    /// </summary>
    [TestClass]
    public class TestUPathStep
    {
        private const string MptIssuanceId = "00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2";

        [TestMethod]
        [TestCategory("TestU")]
        public void TestUPathStepTypeDeserializesAsFlags()
        {
            // shape of mainnet tx 1D813B78FC55ABF9054AEBD2AF9DD7C90361F9985B7897E8E9A592D63BF0CC43
            string json = @"{""currency"":""4249547800000000000000000000000000000000"",""issuer"":""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"",""type"":48}";

            Path step = JsonSerializer.Deserialize<Path>(json, XrplJsonOptions.Default);

            Assert.AreEqual(PathStepType.Currency | PathStepType.Issuer, step.Type);
            Assert.IsTrue(step.Type.Value.HasFlag(PathStepType.Issuer));
            Assert.IsFalse(step.Type.Value.HasFlag(PathStepType.Account));
        }

        [TestMethod]
        [TestCategory("TestU")]
        public void TestUPathStepMptTypeDeserializesAsFlags()
        {
            string json = @"{""mpt_issuance_id"":""" + MptIssuanceId + @""",""issuer"":""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"",""type"":96}";

            Path step = JsonSerializer.Deserialize<Path>(json, XrplJsonOptions.Default);

            Assert.AreEqual(MptIssuanceId, step.MPTokenIssuanceID);
            Assert.AreEqual(PathStepType.MPTokenIssuanceID | PathStepType.Issuer, step.Type);
        }

        [TestMethod]
        [TestCategory("TestU")]
        public void TestUPathStepTypeStaysNumericOnTheWire()
        {
            Path step = new Path
            {
                CurrencyCode = "4249547800000000000000000000000000000000",
                Issuer = "rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3",
                Type = PathStepType.Currency | PathStepType.Issuer,
            };

            string json = JsonSerializer.Serialize(step, XrplJsonOptions.Default);

            StringAssert.Contains(json, @"""type"":48", $"type must serialize as the number rippled sends. Got: {json}");
        }

        [TestMethod]
        [TestCategory("TestU")]
        public void TestUPathStepUndeclaredTypeBitSurvives()
        {
            // a future protocol bit the enum does not name must not break deserialization
            Path step = JsonSerializer.Deserialize<Path>(@"{""type"":176}", XrplJsonOptions.Default);

            Assert.AreEqual(176u, (uint)step.Type.Value);
            Assert.IsTrue(step.Type.Value.HasFlag(PathStepType.Issuer));
        }

        [TestMethod]
        [TestCategory("TestU")]
        public void TestUPathStepValidationMatchesRippledToStrand()
        {
            // rippled toStrand(): hasAccount && (hasIssuer || hasCurrency) -> temBAD_PATH,
            // hasMPT && (hasCurrency || hasAccount) -> temBAD_PATH
            Assert.IsTrue(Validation.IsPathStep(Step(("account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"))), "account-only step is valid");
            Assert.IsTrue(Validation.IsPathStep(Step(("currency", "USD"), ("issuer", "rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"))), "currency+issuer step is valid");
            Assert.IsTrue(Validation.IsPathStep(Step(("mpt_issuance_id", MptIssuanceId))), "MPT step is valid");
            Assert.IsTrue(Validation.IsPathStep(Step(("mpt_issuance_id", MptIssuanceId), ("issuer", "rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"))), "MPT+issuer step is valid");

            Assert.IsFalse(Validation.IsPathStep(Step(("account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"), ("currency", "USD"))), "account+currency is temBAD_PATH");
            Assert.IsFalse(Validation.IsPathStep(Step(("account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"), ("issuer", "rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"))), "account+issuer is temBAD_PATH");
            Assert.IsFalse(Validation.IsPathStep(Step(("account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"), ("mpt_issuance_id", MptIssuanceId))), "account+MPT is temBAD_PATH");
            Assert.IsFalse(Validation.IsPathStep(Step(("currency", "USD"), ("mpt_issuance_id", MptIssuanceId))), "currency+MPT is temBAD_PATH");
            Assert.IsFalse(Validation.IsPathStep(Step()), "an empty step carries no asset and no account");
        }

        private static Dictionary<string, object> Step(params (string Key, object Value)[] fields)
        {
            Dictionary<string, object> step = new Dictionary<string, object>();
            foreach ((string key, object value) in fields)
            {
                step[key] = value;
            }
            return step;
        }

        [TestMethod]
        [TestCategory("TestU")]
        public void TestUPathStepWithoutTypeIsNull()
        {
            Path step = JsonSerializer.Deserialize<Path>(@"{""account"":""rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn""}", XrplJsonOptions.Default);

            Assert.IsNull(step.Type);
        }
    }
}
