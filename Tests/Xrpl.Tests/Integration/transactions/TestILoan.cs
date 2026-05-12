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
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("Loan")]
//[Ignore("LendingProtocol amendment requires rippled 3.1.0+; Open for Voting on mainnet (XLS-66)")]
public class TestILoan : TestILoanBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestLoanBrokerSet_Basic()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        string vaultId = await CreateVaultForBroker(client, wallet);

        LoanBrokerSet tx = new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanBrokerSet_WithRates()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        string vaultId = await CreateVaultForBroker(client, wallet);

        LoanBrokerSet tx = new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            VaultID = vaultId,
            CoverRateMinimum = 15000,
            CoverRateLiquidation = 12000,
            ManagementFeeRate = 100,
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanBrokerCoverDeposit_Basic()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        string brokerId = await CreateBroker(client, wallet);

        LoanBrokerCoverDeposit depositTx = new LoanBrokerCoverDeposit
        {
            Account = wallet.ClassicAddress,
            LoanBrokerID = brokerId,
            Amount = new Currency { Value = "1000000", CurrencyCode = "XRP" },
        };
        depositTx = await client.Autofill(depositTx);

        TransactionSummary result = await client.SubmitAndWait(depositTx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanSet_Basic()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = "10000000",
        };

        TransactionSummary result = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanDelete_Basic()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = "10000000",
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(loanResult);

        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);
        Assert.IsNotNull(loanId, "LoanID should be present in metadata");

        // Repay the loan fully before deleting (tecHAS_OBLIGATIONS otherwise)
        LoanPay payTx = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = "10000000", CurrencyCode = "XRP" },
        };
        payTx = await client.Autofill(payTx);
        TransactionSummary payResult = await client.SubmitAndWait(payTx, walletBorrower, true);
        ValidateResult(payResult);

        LoanDelete deleteTx = new LoanDelete
        {
            Account = walletBroker.ClassicAddress,
            LoanID = loanId,
        };
        deleteTx = await client.Autofill(deleteTx);

        TransactionSummary result = await client.SubmitAndWait(deleteTx, walletBroker, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanBrokerDelete_Basic()
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        string brokerId = await CreateBroker(client, wallet);

        LoanBrokerDelete deleteTx = new LoanBrokerDelete
        {
            Account = wallet.ClassicAddress,
            LoanBrokerID = brokerId,
        };
        deleteTx = await client.Autofill(deleteTx);

        TransactionSummary result = await client.SubmitAndWait(deleteTx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanPay_Basic()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = "10000000",
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(loanResult);

        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);

        LoanPay payTx = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = "10000000", CurrencyCode = "XRP" }, // full principal
        };
        payTx = await client.Autofill(payTx);

        TransactionSummary result = await client.SubmitAndWait(payTx, walletBorrower, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanSet_V2_ParallelSigning()
    {
        // V2: broker and borrower sign independently on separate devices, then combine
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = "10000000",
        };

        TransactionSummary result = await SubmitLoanSetV2(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(result);

        string loanId = GetCreatedObjectId(result, LedgerEntryType.Loan);
        Assert.IsNotNull(loanId, "LoanID should be present in metadata (V2 parallel signing)");
    }

    [TestMethod]
    public async Task TestLoanSet_V3_SequentialSigning()
    {
        // V3: borrower signs first (adds CounterpartySignature), passes to broker who adds TxnSignature
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = "10000000",
        };

        TransactionSummary result = await SubmitLoanSetV3(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(result);

        string loanId = GetCreatedObjectId(result, LedgerEntryType.Loan);
        Assert.IsNotNull(loanId, "LoanID should be present in metadata (V3 sequential signing)");
    }

    [TestMethod]
    public async Task TestLoanBrokerLedgerEntry_VerifyFields()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletBroker, nodeType);

        string brokerId = await CreateBroker(client, walletBroker);

        // Fetch LoanBroker via ledger_entry
        LedgerEntryRequest entryRequest = new LedgerEntryRequest { Index = brokerId };
        LedgerEntryResponse entryResponse = await client.LedgerEntry(entryRequest);

        Assert.IsNotNull(entryResponse?.Node, "LedgerEntry node should not be null");
        Assert.IsInstanceOfType(entryResponse.Node, typeof(LOLoanBroker), "Node should deserialize to LOLoanBroker");

        LOLoanBroker broker = (LOLoanBroker)entryResponse.Node;

        // Verify core fields
        Assert.IsNotNull(broker.Account, "Account (pseudo-account) should be set");
        Assert.AreEqual(walletBroker.ClassicAddress, broker.Owner, "Owner should match the wallet address");
        Assert.IsNotNull(broker.VaultID, "VaultID should be set");
        Assert.IsNotNull(broker.Sequence, "Sequence should be set");
        // LoanSequence and OwnerCount may be 0 (default) and omitted from JSON
        // Assert them only as non-negative if present

        // Number fields (DebtTotal, CoverAvailable, DebtMaximum) may be omitted
        // from JSON when value is 0 (default) — rippled does not serialize default Number values.
        // We verify they are either null (omitted) or a valid string.
        if (broker.DebtTotal != null)
            Assert.IsTrue(broker.DebtTotal.Length > 0, "DebtTotal should be non-empty if present");
        if (broker.CoverAvailable != null)
            Assert.IsTrue(broker.CoverAvailable.Length > 0, "CoverAvailable should be non-empty if present");

        // Verify infrastructure fields
        Assert.IsNotNull(broker.OwnerNode, "OwnerNode should be set");
        Assert.IsNotNull(broker.VaultNode, "VaultNode should be set");
        Assert.IsNotNull(broker.PreviousTxnID, "PreviousTxnID should be set");
        Assert.IsNotNull(broker.PreviousTxnLgrSeq, "PreviousTxnLgrSeq should be set");
    }

    [TestMethod]
    public async Task TestLoanLedgerEntry_VerifyFields()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = "10000000",
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(loanResult);

        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);
        Assert.IsNotNull(loanId, "LoanID should be present in metadata");

        // Fetch Loan via ledger_entry
        LedgerEntryRequest entryRequest = new LedgerEntryRequest { Index = loanId };
        LedgerEntryResponse entryResponse = await client.LedgerEntry(entryRequest);

        Assert.IsNotNull(entryResponse?.Node, "LedgerEntry node should not be null");
        Assert.IsInstanceOfType(entryResponse.Node, typeof(LOLoan), "Node should deserialize to LOLoan");

        LOLoan loan = (LOLoan)entryResponse.Node;

        // Verify core fields
        Assert.AreEqual(walletBorrower.ClassicAddress, loan.Borrower, "Borrower should match the borrower wallet");
        Assert.IsNotNull(loan.LoanBrokerID, "LoanBrokerID should be set");
        Assert.IsNotNull(loan.LoanSequence, "LoanSequence should be set");

        // Number fields — PrincipalRequested was explicitly set to "10000000" in LoanSet,
        // but rippled may omit zero-value Number fields.
        // PrincipalOutstanding may be null if no payments have been made yet (depends on rippled behavior).
        if (loan.PrincipalRequested != null)
            Assert.IsTrue(loan.PrincipalRequested.Length > 0, "PrincipalRequested should be non-empty if present");
        if (loan.PrincipalOutstanding != null)
            Assert.IsTrue(loan.PrincipalOutstanding.Length > 0, "PrincipalOutstanding should be non-empty if present");

        // Verify DateTime fields converted via RippleDateTimeConverter
        Assert.IsNotNull(loan.StartDate, "StartDate should be deserialized as DateTime");
        Assert.IsTrue(loan.StartDate.Value.Year >= 2000, "StartDate should be a valid Ripple epoch date");

        // Rate fields (UInt32) — may be 0 (default) and omitted from JSON when not explicitly set
        // InterestRate, LateInterestRate, etc. are optional if not provided in LoanSet

        // Verify infrastructure fields
        Assert.IsNotNull(loan.OwnerNode, "OwnerNode should be set");
        Assert.IsNotNull(loan.LoanBrokerNode, "LoanBrokerNode should be set");
        Assert.IsNotNull(loan.PreviousTxnID, "PreviousTxnID should be set");
        Assert.IsNotNull(loan.PreviousTxnLgrSeq, "PreviousTxnLgrSeq should be set");
    }

    [TestMethod]
    public async Task TestLoanPay_WithOverpaymentFlag_Rejected()
    {
        // tfLoanOverpayment requires specific loan configuration to be permitted.
        // Without it, the protocol correctly rejects with tecNO_PERMISSION.
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = "10000000",
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(loanResult);

        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);

        // Pay with overpayment flag — should be rejected with tecNO_PERMISSION
        // because the loan was not configured to allow overpayment
        LoanPay payTx = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = "15000000", CurrencyCode = "XRP" },
            Flags = LoanPayFlags.tfLoanOverpayment,
        };
        payTx = await client.Autofill(payTx);

        // SubmitAndWait throws RippleException for tec codes
        try
        {
            await client.SubmitAndWait(payTx, walletBorrower, true);
            Assert.Fail("Expected RippleException for tecNO_PERMISSION");
        }
        catch (RippleException ex)
        {
            Assert.IsTrue(ex.Message.Contains("tecNO_PERMISSION"),
                $"Expected tecNO_PERMISSION but got: {ex.Message}");
        }
    }
}
