using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// LoanSet submitted by the borrower and co-signed by the broker owner. rippled takes the
/// counterparty as <c>Counterparty</c>, or the LoanBroker's owner when omitted, and needs one of
/// <c>Account</c> and the counterparty to be the owner (<c>LoanSet::preclaim</c>,
/// <c>LoanSet::checkSign</c>), so the borrower may submit as well as the broker owner.
/// </summary>
[TestClass]
[TestCategory("Loan")]
public class TestILoanBorrowerSubmits : TestILoanBase
{
    private static IXrplClient client;
    private static bool lendingProtocolActive;

    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
        lendingProtocolActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.LendingProtocol);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestInitialize]
    public void CheckAmendment()
    {
        if (!lendingProtocolActive)
            Assert.Inconclusive("LendingProtocol is not enabled on the test node.");
    }

    [TestMethod]
    public async Task TestLoanSet_BorrowerSubmits_BrokerOwnerCoSigns()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBorrower.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBroker.ClassicAddress,
            PrincipalRequested = 10000000,
        };

        JsonObject prepared = await PrepareLoanSet(client, loanTx, walletBorrower);
        // The broker owner's wallet first: the helper assigns the roles by Account, not by position
        SignatureResult signed = LoanSigningHelper.SignLoanSet(prepared, walletBroker, walletBorrower);
        AssertRoles(signed.TxBlob, walletBorrower, walletBroker);

        TransactionSummary result = await SubmitSignedLoanSet(client, signed.TxBlob);
        ValidateResult(result);
        await AssertBorrowerAsync(GetCreatedObjectId(result, LedgerEntryType.Loan), walletBorrower);
    }

    [TestMethod]
    public async Task TestLoanSet_BorrowerSubmits_CounterpartyOmitted_BrokerOwnerCoSigns()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBorrower.ClassicAddress,
            LoanBrokerID = brokerId,
            PrincipalRequested = 10000000,
        };

        JsonObject prepared = await PrepareLoanSet(client, loanTx, walletBorrower);
        SignatureResult signed = LoanSigningHelper.SignLoanSet(prepared, walletBorrower, walletBroker, walletBroker.ClassicAddress);
        AssertRoles(signed.TxBlob, walletBorrower, walletBroker);

        TransactionSummary result = await SubmitSignedLoanSet(client, signed.TxBlob);
        ValidateResult(result);
        await AssertBorrowerAsync(GetCreatedObjectId(result, LedgerEntryType.Loan), walletBorrower);
    }

    [TestMethod]
    public async Task TestLoanSet_BorrowerSubmits_BrokerOwnerSignsFirst()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBorrower.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBroker.ClassicAddress,
            PrincipalRequested = 10000000,
        };

        JsonObject prepared = await PrepareLoanSet(client, loanTx, walletBorrower);
        SignatureResult withCounterparty = walletBroker.SignAsLoanCounterparty(
            JsonSerializer.Deserialize<Dictionary<string, object>>(prepared.ToJsonString(), XrplJsonOptions.Default));
        SignatureResult signed = LoanSigningHelper.SubmitterSign(withCounterparty.TxBlob, walletBorrower);
        AssertRoles(signed.TxBlob, walletBorrower, walletBroker);

        TransactionSummary result = await SubmitSignedLoanSet(client, signed.TxBlob);
        ValidateResult(result);
        await AssertBorrowerAsync(GetCreatedObjectId(result, LedgerEntryType.Loan), walletBorrower);
    }

    private static void AssertRoles(string txBlob, XrplWallet submitter, XrplWallet counterparty)
    {
        JsonObject decoded = XrplBinaryCodec.Decode(txBlob).AsObject();
        Assert.AreEqual(submitter.ClassicAddress, decoded["Account"]!.GetValue<string>());
        Assert.AreEqual(submitter.PublicKey, decoded["SigningPubKey"]!.GetValue<string>(), "the borrower signs the transaction");
        Assert.AreEqual(counterparty.PublicKey, decoded["CounterpartySignature"]!["SigningPubKey"]!.GetValue<string>(), "the broker owner co-signs");
    }

    private static async Task AssertBorrowerAsync(string loanId, XrplWallet borrower)
    {
        Assert.IsNotNull(loanId, "LoanID should be present in metadata");

        LedgerEntryResponse entry = await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed();
        Assert.IsInstanceOfType(entry?.Node, typeof(LOLoan));
        Assert.AreEqual(borrower.ClassicAddress, ((LOLoan)entry.Node).Borrower, "the submitting borrower must hold the loan");
    }
}
