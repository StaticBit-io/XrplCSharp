using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestUVaultShares
{
    [TestMethod]
    public void RedeemShares_NoSharesOutstanding_IsThePathDryTheNodeReturns()
    {
        // Assets left in a vault with no shares outstanding: rippled's sharesToAssetsWithdraw divides
        // by zero, which its Number reports as an overflow and VaultWithdraw turns into tecPATH_DRY.
        LOVault vault = new LOVault
        {
            Asset = new IssuedCurrency { Currency = "XRP" },
            AssetsTotal = 5,
            AssetsAvailable = 5,
        };

        VaultQuote quote = VaultShares.RedeemShares(vault, sharesOutstanding: 0, holderShares: 0, shares: 1);

        Assert.AreEqual("tecPATH_DRY", quote.Refusal);
        Assert.IsFalse(quote.IsAccepted);
    }

    [TestMethod]
    public void Deposit_EmptyVault_MintsSharesAtTheVaultScale()
    {
        LOVault vault = new LOVault
        {
            Asset = new IssuedCurrency { Currency = "USD", Issuer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
            Scale = 6,
        };

        VaultQuote quote = VaultShares.Deposit(vault, sharesOutstanding: 0, amount: XrplNumber.Parse("1234.5678"));

        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        Assert.AreEqual(1_234_567_800m, quote.Shares);
        Assert.AreEqual(1234.5678m, quote.Assets);
    }
}
