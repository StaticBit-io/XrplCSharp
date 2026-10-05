using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Methods;
using Xrpl.Sugar;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestULedgerRules
{
    private static ServerFeatures Enabled(params string[] names)
    {
        Dictionary<string, FeatureInfo> features = new Dictionary<string, FeatureInfo>();
        for (int i = 0; i < names.Length; i++)
            features[i.ToString("X64")] = new FeatureInfo { Name = names[i], Enabled = true };

        return new ServerFeatures { Features = features };
    }

    /// <summary>
    /// <c>setCurrentTransactionRules</c>: SingleAssetVault, LendingProtocol and MPTokensV2 each
    /// switch <c>Number</c> to the large mantissa; without them it stays small.
    /// </summary>
    [TestMethod]
    [DataRow("SingleAssetVault")]
    [DataRow("LendingProtocol")]
    [DataRow("MPTokensV2")]
    public void LargeNumbersFollowEachAmendmentThatSelectsThem(string amendment)
    {
        LedgerRules rules = LedgerRules.FromFeatures(Enabled(amendment, "fixCleanup3_3_0"));

        Assert.IsTrue(rules.LargeNumbers);
        Assert.AreEqual(NumberMantissaScale.Large330, rules.Context.Scale);
    }

    [TestMethod]
    public void NumbersStaySmallWithoutThoseAmendments()
    {
        LedgerRules rules = LedgerRules.FromFeatures(Enabled("fixCleanup3_3_0", "fixAMMv1_3"));

        Assert.IsFalse(rules.LargeNumbers);
        Assert.IsFalse(rules.MPTokensV2);
        Assert.AreEqual(NumberMantissaScale.Small, rules.Context.Scale);
    }

    [TestMethod]
    public void MPTokensV2IsReadAlongsideTheScale()
    {
        LedgerRules rules = LedgerRules.FromFeatures(Enabled("MPTokensV2", "fixCleanup3_2_0"));

        Assert.IsTrue(rules.MPTokensV2);
        Assert.AreEqual(NumberMantissaScale.Large320, rules.Context.Scale);
    }
}
