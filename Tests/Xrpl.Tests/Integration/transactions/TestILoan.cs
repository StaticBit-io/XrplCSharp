using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
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
