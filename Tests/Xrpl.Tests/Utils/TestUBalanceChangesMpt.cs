using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Utils;

namespace XrplTests.Utils;

/// <summary>
/// MPT balance changes, read from <c>simulate</c> responses of a standalone node
/// (<c>Fixtures/Simulate</c>) and from metadata built to hit the ambiguous case.
/// </summary>
[TestClass]
public class TestUBalanceChangesMpt
{
    private const string Depositor = "rQsTnntg36bd8epF47qzhgAyQzjDLDd6Dc";
    private const string VaultPseudoAccount = "rNLaas7dZix3WviwSq41WDQYxCtvnqyFGQ";
    private const string ShareId = "000000019235C8EA92D9BC14E403C767CFDDC1A7C56AFDDD";

    internal static SimulateResponse LoadSimulate(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Simulate", name);
        return JsonSerializer.Deserialize<SimulateResponse>(File.ReadAllText(path), XrplJsonOptions.Default);
    }

    private static Currency Single(Dictionary<string, List<Currency>> changes, string account, Func<Currency, bool> match)
    {
        Assert.IsTrue(changes.ContainsKey(account), $"no changes for {account}");
        return changes[account].Single(match);
    }

    [TestMethod]
    public void TestUVaultDeposit_CreatedShareToken()
    {
        Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(LoadSimulate("vault-deposit-xrp.json").Meta);

        Assert.AreEqual("10000000", Single(changes, Depositor, c => c.MPTokenIssuanceID == ShareId).Value);
        Assert.AreEqual("-10000012", Single(changes, Depositor, c => c.CurrencyCode == "XRP").Value);
        Assert.AreEqual("-10000000", Single(changes, VaultPseudoAccount, c => c.MPTokenIssuanceID == ShareId).Value);
        Assert.AreEqual("10000000", Single(changes, VaultPseudoAccount, c => c.CurrencyCode == "XRP").Value);
    }

    [TestMethod]
    public void TestUVaultWithdraw_ModifiedShareToken()
    {
        Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(LoadSimulate("vault-withdraw-xrp.json").Meta);

        Assert.AreEqual("-3000000", Single(changes, Depositor, c => c.MPTokenIssuanceID == ShareId).Value);
        Assert.AreEqual("2999988", Single(changes, Depositor, c => c.CurrencyCode == "XRP").Value);
        Assert.AreEqual("3000000", Single(changes, VaultPseudoAccount, c => c.MPTokenIssuanceID == ShareId).Value);
    }

    [TestMethod]
    public void TestUMptAmountAddedToExistingToken_IsResolvedFromOutstandingAmount()
    {
        // The holder's MPToken existed with no amount, so the metadata lists no previous MPTAmount.
        Meta meta = JsonSerializer.Deserialize<Meta>(AmbiguousMeta(outstandingBefore: "5", outstandingAfter: "12"), XrplJsonOptions.Default);

        Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(meta);

        Assert.AreEqual("7", Single(changes, Depositor, c => c.MPTokenIssuanceID == ShareId).Value);
        Assert.AreEqual("-7", Single(changes, VaultPseudoAccount, c => c.MPTokenIssuanceID == ShareId).Value);
    }

    [TestMethod]
    public void TestUMptTokenTouchedWithoutAmountChange_ReportsNothing()
    {
        // Same shape, but the issuance's outstanding amount did not move: the holder's amount did not either.
        Meta meta = JsonSerializer.Deserialize<Meta>(AmbiguousMeta(outstandingBefore: null, outstandingAfter: "12"), XrplJsonOptions.Default);

        Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(meta);

        Assert.IsFalse(changes.ContainsKey(Depositor) && changes[Depositor].Any(c => c.MPTokenIssuanceID == ShareId));
        Assert.IsFalse(changes.ContainsKey(VaultPseudoAccount));
    }

    private static string AmbiguousMeta(string outstandingBefore, string outstandingAfter)
    {
        string previous = outstandingBefore == null
            ? string.Empty
            : $@"""PreviousFields"": {{ ""OutstandingAmount"": ""{outstandingBefore}"" }},";

        return $@"{{
            ""TransactionIndex"": 0,
            ""TransactionResult"": ""tesSUCCESS"",
            ""AffectedNodes"": [
                {{ ""ModifiedNode"": {{
                    ""LedgerEntryType"": ""MPToken"",
                    ""LedgerIndex"": ""0000000000000000000000000000000000000000000000000000000000000001"",
                    ""PreviousFields"": {{ ""Flags"": 0 }},
                    ""FinalFields"": {{ ""Account"": ""{Depositor}"", ""Flags"": 2, ""MPTAmount"": ""7"", ""MPTokenIssuanceID"": ""{ShareId}"", ""OwnerNode"": ""0"" }}
                }} }},
                {{ ""ModifiedNode"": {{
                    ""LedgerEntryType"": ""MPTokenIssuance"",
                    ""LedgerIndex"": ""0000000000000000000000000000000000000000000000000000000000000002"",
                    {previous}
                    ""FinalFields"": {{ ""Flags"": 56, ""Issuer"": ""{VaultPseudoAccount}"", ""OutstandingAmount"": ""{outstandingAfter}"", ""OwnerNode"": ""0"", ""Sequence"": 1 }}
                }} }}
            ]
        }}";
    }
}
