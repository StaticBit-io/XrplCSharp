using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text;
using System.Threading.Tasks;

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

/// <summary>
/// Integration tests for Credential transactions (CredentialCreate, CredentialAccept, CredentialDelete).
/// Tests the complete lifecycle of Credentials on DevNet.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TestICredential
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;

    static XrplWallet walletIssuer;
    static XrplWallet walletSubject;
    public static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);

        walletIssuer = XrplWallet.Generate();
        walletSubject = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletSubject);
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    #region CredentialCreate Tests

    /// <summary>
    /// Tests creating a credential with required fields only (Subject, CredentialType).
    /// </summary>
    [TestMethod]
    public async Task TestCredentialCreate_Basic()
    {
        var credTypeHex = ToHex("cred_basic_test");

        var tx = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    /// <summary>
    /// Tests creating a credential with the optional URI field.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialCreate_WithURI()
    {
        var credTypeHex = ToHex("cred_uri_test");
        var uriHex = ToHex("https://example.com/credential");

        var tx = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
            URI = uriHex,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    /// <summary>
    /// Tests creating a credential with the optional Expiration field.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialCreate_WithExpiration()
    {
        var credTypeHex = ToHex("cred_expiration_test");

        var tx = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
            Expiration = DateTime.UtcNow.AddHours(1),
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    /// <summary>
    /// Tests creating a self-issued credential where Account == Subject.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialCreate_SelfIssued()
    {
        var credTypeHex = ToHex("cred_self_issued_test");

        var tx = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletIssuer.ClassicAddress,
            CredentialType = credTypeHex,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    #endregion

    #region CredentialAccept Tests

    /// <summary>
    /// Tests creating a credential then accepting it with the subject wallet.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialAccept_Basic()
    {
        var credTypeHex = ToHex("cred_accept_basic_test");

        var createTx = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var acceptTx = new CredentialAccept
        {
            Account = walletSubject.ClassicAddress,
            Issuer = walletIssuer.ClassicAddress,
            CredentialType = credTypeHex,
        };
        acceptTx = await client.Autofill(acceptTx);

        var acceptResult = await client.SubmitAndWait(acceptTx, walletSubject, true);
        ValidateResult(acceptResult);
    }

    #endregion

    #region CredentialDelete Tests

    /// <summary>
    /// Tests issuer revoking an accepted credential.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialDelete_ByIssuer()
    {
        var credTypeHex = ToHex("cred_del_issuer_test");

        await TryCreateAndAcceptCredential(walletIssuer, walletSubject, credTypeHex);

        var deleteTx = new CredentialDelete
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        deleteTx = await client.Autofill(deleteTx);

        var result = await client.SubmitAndWait(deleteTx, walletIssuer, true);
        ValidateResult(result);
    }

    /// <summary>
    /// Tests subject removing their own credential.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialDelete_BySubject()
    {
        var credTypeHex = ToHex("cred_del_subject_test");

        await TryCreateAndAcceptCredential(walletIssuer, walletSubject, credTypeHex);

        var deleteTx = new CredentialDelete
        {
            Account = walletSubject.ClassicAddress,
            Issuer = walletIssuer.ClassicAddress,
            CredentialType = credTypeHex,
        };
        deleteTx = await client.Autofill(deleteTx);

        var result = await client.SubmitAndWait(deleteTx, walletSubject, true);
        ValidateResult(result);
    }

    #endregion

    #region Lifecycle Tests

    /// <summary>
    /// Tests the complete credential lifecycle: Create, Accept, verify exists, Delete, verify gone.
    /// </summary>
    [TestMethod]
    public async Task TestCredential_FullLifecycle()
    {
        var credTypeHex = ToHex("cred_lifecycle_test");

        var createTx = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);
        Console.WriteLine("Step 1: Credential created successfully");

        var acceptTx = new CredentialAccept
        {
            Account = walletSubject.ClassicAddress,
            Issuer = walletIssuer.ClassicAddress,
            CredentialType = credTypeHex,
        };
        acceptTx = await client.Autofill(acceptTx);
        var acceptResult = await client.SubmitAndWait(acceptTx, walletSubject, true);
        ValidateResult(acceptResult);
        Console.WriteLine("Step 2: Credential accepted successfully");

        var existsAfterAccept = await VerifyCredentialExists(walletIssuer.ClassicAddress, walletSubject.ClassicAddress, credTypeHex);
        Assert.IsTrue(existsAfterAccept, "Credential should exist after accept");
        Console.WriteLine("Step 3: Credential verified to exist");

        var deleteTx = new CredentialDelete
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        deleteTx = await client.Autofill(deleteTx);
        var deleteResult = await client.SubmitAndWait(deleteTx, walletIssuer, true);
        ValidateResult(deleteResult);
        Console.WriteLine("Step 4: Credential deleted successfully");

        var existsAfterDelete = await VerifyCredentialExists(walletIssuer.ClassicAddress, walletSubject.ClassicAddress, credTypeHex);
        Assert.IsFalse(existsAfterDelete, "Credential should not exist after deletion");
        Console.WriteLine("Step 5: Credential verified to be gone");

        Console.WriteLine("Credential full lifecycle test completed successfully!");
    }

    #endregion

    #region Negative Tests

    /// <summary>
    /// Tests that accepting a credential that was never created fails with RippleException.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialAccept_FailsForNonexistent()
    {
        var credTypeHex = ToHex("cred_nonexistent_test");

        var acceptTx = new CredentialAccept
        {
            Account = walletSubject.ClassicAddress,
            Issuer = walletIssuer.ClassicAddress,
            CredentialType = credTypeHex,
        };
        acceptTx = await client.Autofill(acceptTx);

        await Helper.ThrowsExceptionAsync<RippleException>(
            () => client.SubmitAndWait(acceptTx, walletSubject, true));
    }

    /// <summary>
    /// Tests that creating the same credential twice fails with tecDUPLICATE.
    /// </summary>
    [TestMethod]
    public async Task TestCredentialCreate_DuplicateFails()
    {
        var credTypeHex = ToHex("cred_duplicate_test");

        var tx1 = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        tx1 = await client.Autofill(tx1);
        var result1 = await client.SubmitAndWait(tx1, walletIssuer, true);
        ValidateResult(result1);

        var tx2 = new CredentialCreate
        {
            Account = walletIssuer.ClassicAddress,
            Subject = walletSubject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        tx2 = await client.Autofill(tx2);

        await Helper.ThrowsExceptionAsync<RippleException>(
            () => client.SubmitAndWait(tx2, walletIssuer, true),
            "Final tx result is not success: tecDUPLICATE");
    }

    #endregion

    #region Helper Methods

    private static string ToHex(string text)
    {
        return BitConverter.ToString(Encoding.UTF8.GetBytes(text)).Replace("-", "");
    }

    private static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
        {
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
        }
    }

    private static async Task TryCreateAndAcceptCredential(XrplWallet issuer, XrplWallet subject, string credTypeHex, string uri = null)
    {
        var create = new CredentialCreate
        {
            Account = issuer.ClassicAddress,
            Subject = subject.ClassicAddress,
            CredentialType = credTypeHex,
        };
        if (uri != null) create.URI = uri;
        create = await client.Autofill(create);
        await client.SubmitAndWait(create, issuer, true);

        var accept = new CredentialAccept
        {
            Account = subject.ClassicAddress,
            Issuer = issuer.ClassicAddress,
            CredentialType = credTypeHex,
        };
        accept = await client.Autofill(accept);
        await client.SubmitAndWait(accept, subject, true);
    }

    private async Task<bool> VerifyCredentialExists(string issuer, string subject, string credTypeHex)
    {
        try
        {
            var request = new AccountObjectsRequest(subject)
            {
                LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
            };
            var response = await client.AccountObjects(request);
            if (response?.AccountObjectList != null)
            {
                foreach (var obj in response.AccountObjectList)
                {
                    if (obj.LedgerEntryType == LedgerEntryType.Credential)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }


    #endregion
}
