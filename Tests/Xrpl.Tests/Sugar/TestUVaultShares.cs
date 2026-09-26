using System;

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

    private static readonly DateTime Subscription = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Redemption = Subscription.AddDays(30);

    private static LOVault ClosedEnded() => new LOVault
    {
        Asset = new IssuedCurrency { Currency = "XRP" },
        AssetsTotal = 1_000_000,
        AssetsAvailable = 1_000_000,
        VaultKind = (uint)VaultKind.ClosedEnded,
        SubscriptionDate = Subscription,
        RedemptionDate = Redemption,
    };

    [TestMethod]
    [DataRow(0, null)]
    [DataRow(1, "tecEXPIRED")]
    [DataRow(30 * 24 * 3600, "tecEXPIRED")]
    public void Deposit_ClosedEnded_OnlyInTheSubscriptionPhase(int secondsAfterSubscription, string refusal)
    {
        // getVaultPhase: the subscription phase includes SubscriptionDate itself.
        VaultQuote quote = VaultShares.Deposit(
            ClosedEnded(), 1_000_000, 1_000, parentCloseTime: Subscription.AddSeconds(secondsAfterSubscription));

        Assert.AreEqual(refusal, quote.Refusal, quote.RefusalReason);
    }

    [TestMethod]
    [DataRow(0, null)]
    [DataRow(1, "tecTOO_SOON")]
    [DataRow(30 * 24 * 3600 - 1, "tecTOO_SOON")]
    [DataRow(30 * 24 * 3600, null)]
    public void Withdraw_ClosedEnded_NotInTheInvestmentPhase(int secondsAfterSubscription, string refusal)
    {
        // The investment phase runs from after SubscriptionDate until RedemptionDate, which already
        // belongs to the redemption phase.
        DateTime at = Subscription.AddSeconds(secondsAfterSubscription);

        Assert.AreEqual(refusal, VaultShares.WithdrawAssets(ClosedEnded(), 1_000_000, 1_000, 1_000, parentCloseTime: at).Refusal);
        Assert.AreEqual(refusal, VaultShares.RedeemShares(ClosedEnded(), 1_000_000, 1_000, 1_000, parentCloseTime: at).Refusal);
    }

    [TestMethod]
    public void ClosedEnded_WithoutATimeOrTheAmendment_SkipsThePhases()
    {
        DateTime investment = Subscription.AddDays(1);

        Assert.IsNull(VaultShares.Deposit(ClosedEnded(), 1_000_000, 1_000).Refusal);
        Assert.IsNull(VaultShares.Deposit(ClosedEnded(), 1_000_000, 1_000, rules: new LedgerRules { LendingProtocolV1_1 = false }, parentCloseTime: investment).Refusal);
    }
}
