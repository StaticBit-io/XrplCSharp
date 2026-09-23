using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Checks every <see cref="Hashes"/> ledger-object helper against the index the node itself
/// assigns, read back through <c>account_objects</c> / <c>account_info</c>.
/// </summary>
[TestClass]
public class TestILedgerObjectIds
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;

    private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    [TestMethod]
    public async Task TestAccountRootId()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet);

        AccountInfo info = await client.AccountInfo(new AccountInfoRequest(wallet.ClassicAddress)).Typed();

        Assert.AreEqual(info.AccountData.Index, wallet.ClassicAddress.HashAccountRoot());
    }

    [TestMethod]
    public async Task TestOfferId()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet);

        OfferCreate tx = new OfferCreate
        {
            Account = wallet.ClassicAddress,
            TakerGets = new Currency { ValueAsXrp = 10 },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = wallet.ClassicAddress, Value = "10" },
        };
        tx = await client.Autofill(tx);
        ValidateResult(await client.SubmitAndWait(tx, wallet, true));

        string nodeIndex = await SingleObjectIndex(wallet.ClassicAddress, LedgerEntryType.Offer);

        Assert.AreEqual(nodeIndex, Hashes.HashOfferId(wallet.ClassicAddress, tx.Sequence!.Value));
    }

    // The two accounts are picked so that exactly one AccountID has its high bit set: that is the
    // pair a signed comparison puts in the wrong low/high order.
    [TestMethod]
    [DataRow("USD")]
    [DataRow("4841534849445300000000000000000000000000")]
    public async Task TestTrustlineId(string currencyCode)
    {
        XrplWallet issuer = GenerateWallet(highBit: true);
        XrplWallet holder = GenerateWallet(highBit: false);
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, holder);

        TrustSet tx = new TrustSet
        {
            Account = holder.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = currencyCode, Issuer = issuer.ClassicAddress, Value = "1000" },
        };
        tx = await client.Autofill(tx);
        ValidateResult(await client.SubmitAndWait(tx, holder, true));

        string nodeIndex = await SingleObjectIndex(holder.ClassicAddress, LedgerEntryType.RippleState);

        Assert.AreEqual(nodeIndex, Hashes.HashTrustline(holder.ClassicAddress, issuer.ClassicAddress, currencyCode));
        Assert.AreEqual(nodeIndex, Hashes.HashTrustline(issuer.ClassicAddress, holder.ClassicAddress, currencyCode));
    }

    [TestMethod]
    public async Task TestEscrowId()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet destination = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, destination);

        DateTime closeTime = await IntegrationTestConfig.ValidatedCloseTimeAsync(client);
        EscrowCreate tx = new EscrowCreate
        {
            Account = owner.ClassicAddress,
            Destination = destination.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1 },
            FinishAfter = closeTime.AddHours(1),
        };
        tx = await client.Autofill(tx);
        ValidateResult(await client.SubmitAndWait(tx, owner, true));

        string nodeIndex = await SingleObjectIndex(owner.ClassicAddress, LedgerEntryType.Escrow);

        Assert.AreEqual(nodeIndex, Hashes.HashEscrow(owner.ClassicAddress, (int)tx.Sequence!.Value));
    }

    [TestMethod]
    public async Task TestSignerListId()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet);

        SignerListSet tx = new SignerListSet
        {
            Account = wallet.ClassicAddress,
            SignerQuorum = 1,
            SignerEntries = new List<SignerEntryWrapper>
            {
                new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = XrplWallet.Generate().ClassicAddress, SignerWeight = 1 } },
            },
        };
        tx = await client.Autofill(tx);
        ValidateResult(await client.SubmitAndWait(tx, wallet, true));

        string nodeIndex = await SingleObjectIndex(wallet.ClassicAddress, LedgerEntryType.SignerList);

        Assert.AreEqual(nodeIndex, wallet.ClassicAddress.HashSignerListId());
    }

    private static XrplWallet GenerateWallet(bool highBit)
    {
        while (true)
        {
            XrplWallet wallet = XrplWallet.Generate();
            bool isHigh = Convert.ToByte(wallet.ClassicAddress.AddressToHex().Substring(0, 2), 16) >= 0x80;
            if (isHigh == highBit)
            {
                return wallet;
            }
        }
    }

    private static async Task<string> SingleObjectIndex(string account, LedgerEntryType type)
    {
        AccountObjectsRequest request = new AccountObjectsRequest(account) { Type = type };
        AccountObjects response = await client.AccountObjects(request).Typed();
        BaseLedgerEntry entry = response.AccountObjectList.Single();
        return entry.Index;
    }

    private static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" } })
        {
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
        }
    }
}
