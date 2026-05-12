using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("Loan")]
[Ignore("LendingProtocol amendment requires rippled 3.1.0+; Open for Voting on mainnet (XLS-66)")]
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
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet, walletIssuer);

        LoanBrokerSet tx = new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, wallet, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanBrokerSet_WithRates()
    {
        XrplWallet wallet = XrplWallet.Generate();
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet, walletIssuer);

        LoanBrokerSet tx = new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
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
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet, walletIssuer);

        LoanBrokerSet createTx = new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, wallet, true);
        ValidateResult(createResult);

        string brokerId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(brokerId, "LoanBrokerID should be present in metadata");

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
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower, walletIssuer);

        LoanBrokerSet brokerTx = new LoanBrokerSet
        {
            Account = walletBroker.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        brokerTx = await client.Autofill(brokerTx);
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, walletBroker, true);
        ValidateResult(brokerResult);

        string brokerId = GetCreatedObjectId(brokerResult);
        Assert.IsNotNull(brokerId, "LoanBrokerID should be present in metadata");

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Borrower = walletBorrower.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        loanTx = await client.Autofill(loanTx);

        TransactionSummary result = await client.SubmitAndWait(loanTx, walletBroker, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestLoanDelete_Basic()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower, walletIssuer);

        LoanBrokerSet brokerTx = new LoanBrokerSet
        {
            Account = walletBroker.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        brokerTx = await client.Autofill(brokerTx);
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, walletBroker, true);
        ValidateResult(brokerResult);

        string brokerId = GetCreatedObjectId(brokerResult);
        Assert.IsNotNull(brokerId, "LoanBrokerID should be present in metadata");

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Borrower = walletBorrower.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        loanTx = await client.Autofill(loanTx);
        TransactionSummary loanResult = await client.SubmitAndWait(loanTx, walletBroker, true);
        ValidateResult(loanResult);

        string loanId = GetCreatedObjectId(loanResult);

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
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet, walletIssuer);

        LoanBrokerSet createTx = new LoanBrokerSet
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        createTx = await client.Autofill(createTx);
        TransactionSummary createResult = await client.SubmitAndWait(createTx, wallet, true);
        ValidateResult(createResult);

        string brokerId = GetCreatedObjectId(createResult);
        Assert.IsNotNull(brokerId, "LoanBrokerID should be present in metadata");

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
        XrplWallet walletIssuer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower, walletIssuer);

        LoanBrokerSet brokerTx = new LoanBrokerSet
        {
            Account = walletBroker.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        brokerTx = await client.Autofill(brokerTx);
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, walletBroker, true);
        ValidateResult(brokerResult);

        string brokerId = GetCreatedObjectId(brokerResult);

        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Borrower = walletBorrower.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        loanTx = await client.Autofill(loanTx);
        TransactionSummary loanResult = await client.SubmitAndWait(loanTx, walletBroker, true);
        ValidateResult(loanResult);

        string loanId = GetCreatedObjectId(loanResult);

        LoanPay payTx = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = "100", CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress },
        };
        payTx = await client.Autofill(payTx);

        TransactionSummary result = await client.SubmitAndWait(payTx, walletBorrower, true);
        ValidateResult(result);
    }
}
