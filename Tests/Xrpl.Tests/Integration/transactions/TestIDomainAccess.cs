using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIDomainAccess
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;
    public static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;
    private static bool permissionedDomainsActive;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
        permissionedDomainsActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.PermissionedDomains);
    }

    [TestInitialize]
    public void CheckPermissionedDomainsAmendment()
    {
        if (!permissionedDomainsActive)
        {
            Assert.Inconclusive("PermissionedDomains amendment is not enabled on the test node.");
        }
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    private static string ToHex(string text)
    {
        return BitConverter.ToString(Encoding.UTF8.GetBytes(text)).Replace("-", "");
    }

    private static bool ValidateSuccessResultOrSkip(TransactionSummary res, string testName)
    {
        Assert.IsNotNull(res, $"{testName}: Response should not be null");
        Assert.IsNotNull(res.Meta, $"{testName}: Meta should not be null");
        Assert.IsNotNull(res.Meta.TransactionResult, $"{testName}: TransactionResult should not be null");

        var result = res.Meta.TransactionResult;
        Console.WriteLine($"{testName}: Result = {result}");

        if (result == "tesSUCCESS")
        {
            return true;
        }

        if (result == "temDISABLED" || result == "notEnabled")
        {
            Assert.Inconclusive($"{testName}: PermissionedDomains amendment is not enabled on this network.");
            return false;
        }

        Assert.Fail($"{testName}: Unexpected result {result}");
        return false;
    }

    private static string ExtractDomainId(TransactionSummary res)
    {
        if (res.Meta?.AffectedNodes == null)
            return null;
        foreach (var node in res.Meta.AffectedNodes)
        {
            if (node.CreatedNode?.LedgerEntryType == LedgerEntryType.PermissionedDomain)
                return node.CreatedNode.LedgerIndex;
        }
        return null;
    }

    [TestMethod]
    public async Task TestDomainAccess_FullFlow()
    {
        var issuerWallet = XrplWallet.Generate();
        var traderWallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuerWallet, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, traderWallet, nodeType);

        string credTypeHex = ToHex("domain_access_cred");

        var createDomain = new PermissionedDomainSet
        {
            Account = issuerWallet.ClassicAddress,
            AcceptedCredentials = new List<AcceptedCredentialWrapper>
            {
                new AcceptedCredentialWrapper
                {
                    Credential = new AcceptedCredential
                    {
                        Issuer = issuerWallet.ClassicAddress,
                        CredentialType = credTypeHex
                    }
                }
            }
        };

        var autofilledDomain = await client.Autofill(createDomain);
        var resDomain = await client.SubmitAndWait(autofilledDomain, issuerWallet, true);
        if (!ValidateSuccessResultOrSkip(resDomain, "PermissionedDomainSet for domain access test"))
        {
            return;
        }

        string domainId = ExtractDomainId(resDomain);
        Assert.IsNotNull(domainId, "Could not extract DomainID from transaction metadata");
        Console.WriteLine($"Created PermissionedDomain: {domainId}");

        // Step 1: the trader holds no matching credential at all.
        DomainAccessResult noCredential = await client.GetDomainAccess(traderWallet.ClassicAddress, domainId);
        Assert.IsFalse(noCredential.HasAccess, "Trader without credential should not have access");
        Assert.AreEqual(0, noCredential.InvalidCredentials.Count, "No matching credential means an empty invalid list");

        // The domain owner gets no shortcut either: access requires a credential.
        DomainAccessResult ownerAccess = await client.GetDomainAccess(issuerWallet.ClassicAddress, domainId);
        Assert.IsFalse(ownerAccess.HasAccess, "Domain owner without credential should not have access");

        // Step 2: the issuer issues a credential to the trader; it is not accepted yet.
        var credCreate = new CredentialCreate
        {
            Account = issuerWallet.ClassicAddress,
            Subject = traderWallet.ClassicAddress,
            CredentialType = credTypeHex,
        };
        var autofilledCredCreate = await client.Autofill(credCreate);
        var resCredCreate = await client.SubmitAndWait(autofilledCredCreate, issuerWallet, true);
        if (!ValidateSuccessResultOrSkip(resCredCreate, "CredentialCreate for trader"))
        {
            return;
        }

        DomainAccessResult notAccepted = await client.GetDomainAccess(traderWallet.ClassicAddress, domainId);
        Assert.IsFalse(notAccepted.HasAccess, "Unaccepted credential should not grant access");
        Assert.AreEqual(1, notAccepted.InvalidCredentials.Count, "Unaccepted credential should be reported as invalid");
        Assert.AreEqual(issuerWallet.ClassicAddress, notAccepted.InvalidCredentials[0].Issuer);
        Assert.IsFalse(notAccepted.InvalidCredentials[0].Accepted);
        Assert.IsFalse(notAccepted.InvalidCredentials[0].Expired);

        // Step 3: the trader accepts the credential and gains access.
        var credAccept = new CredentialAccept
        {
            Account = traderWallet.ClassicAddress,
            Issuer = issuerWallet.ClassicAddress,
            CredentialType = credTypeHex,
        };
        var autofilledCredAccept = await client.Autofill(credAccept);
        var resCredAccept = await client.SubmitAndWait(autofilledCredAccept, traderWallet, true);
        if (!ValidateSuccessResultOrSkip(resCredAccept, "CredentialAccept for trader"))
        {
            return;
        }

        DomainAccessResult accepted = await client.GetDomainAccess(traderWallet.ClassicAddress, domainId);
        Assert.IsTrue(accepted.HasAccess, "Accepted credential should grant access");
        Assert.AreEqual(0, accepted.InvalidCredentials.Count);
        Console.WriteLine($"Domain access confirmed at ledger {accepted.LedgerIndex}");
    }
}
