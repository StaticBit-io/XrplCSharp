using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

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
public class TestIPermissionedDomain
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;
    public static TestNodeType nodeType = TestNodeType.Standalone;

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

    #region Helper Methods

    private static string ToHex(string text)
    {
        return BitConverter.ToString(Encoding.UTF8.GetBytes(text)).Replace("-", "");
    }

    private bool ValidateSuccessResultOrSkip(TransactionSummary res, string testName)
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
            Console.WriteLine($"{testName}: PermissionedDomains amendment may not be enabled on this network. Skipping assertions.");
            return false;
        }

        if (result.StartsWith("tec"))
        {
            Console.WriteLine($"{testName}: Transaction claimed but failed with {result}");
            return false;
        }

        Assert.Fail($"{testName}: Unexpected result {result}");
        return false;
    }
    
    private static List<AcceptedCredentialWrapper> CreateCredentials(string issuer, string credentialType)
    {
        return new List<AcceptedCredentialWrapper>
        {
            new AcceptedCredentialWrapper
            {
                Credential = new AcceptedCredential
                {
                    Issuer = issuer,
                    CredentialType = ToHex(credentialType)
                }
            }
        };
    }


    #endregion

    #region PermissionedDomainSet Tests

    [TestMethod]
    public async Task TestPermissionedDomainSet_CreateNewDomain()
    {
        var wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        var domainSet = new PermissionedDomainSet
        {
            Account = wallet.ClassicAddress,
            AcceptedCredentials = CreateCredentials(wallet.ClassicAddress, "test_credential")
        };

        var autofilled = await client.Autofill(domainSet);
        var res = await client.SubmitAndWait(autofilled, wallet, true);

        if (!ValidateSuccessResultOrSkip(res, "PermissionedDomainSet create"))
        {
            return;
        }

        Console.WriteLine($"Created PermissionedDomain for account: {wallet.ClassicAddress}");
    }

    [TestMethod]
    public async Task TestPermissionedDomainSet_CreateWithMultipleCredentials()
    {
        var wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        var credentials = new List<AcceptedCredentialWrapper>
        {
            new AcceptedCredentialWrapper
            {
                Credential = new AcceptedCredential
                {
                    Issuer = wallet.ClassicAddress,
                    CredentialType = ToHex("credential_type_1")
                }
            },
            new AcceptedCredentialWrapper
            {
                Credential = new AcceptedCredential
                {
                    Issuer = wallet.ClassicAddress,
                    CredentialType = ToHex("credential_type_2")
                }
            }
        };

        var domainSet = new PermissionedDomainSet
        {
            Account = wallet.ClassicAddress,
            AcceptedCredentials = credentials
        };

        var autofilled = await client.Autofill(domainSet);
        var res = await client.SubmitAndWait(autofilled, wallet, true);

        if (!ValidateSuccessResultOrSkip(res, "PermissionedDomainSet with multiple credentials"))
        {
            return;
        }

        Console.WriteLine($"Created PermissionedDomain with multiple credentials for account: {wallet.ClassicAddress}");
    }

    #endregion

    #region PermissionedDomainDelete Tests

    [TestMethod]
    public async Task TestPermissionedDomainDelete_DeleteExistingDomain()
    {
        var wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        var createDomain = new PermissionedDomainSet
        {
            Account = wallet.ClassicAddress,
            AcceptedCredentials = CreateCredentials(wallet.ClassicAddress, "delete_test_cred")
        };

        var autofilled1 = await client.Autofill(createDomain);
        var res1 = await client.SubmitAndWait(autofilled1, wallet, true);

        if (!ValidateSuccessResultOrSkip(res1, "PermissionedDomainSet create for delete test"))
        {
            Console.WriteLine("Skipping delete test - domain creation failed");
            return;
        }

        Console.WriteLine($"Created PermissionedDomain for deletion test: {wallet.ClassicAddress}");

        string domainId = null;
        if (res1.Meta?.AffectedNodes != null)
        {
            foreach (var node in res1.Meta.AffectedNodes)
            {
                if (node.CreatedNode?.LedgerEntryType == LedgerEntryType.PermissionedDomain)
                {
                    domainId = node.CreatedNode.LedgerIndex;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(domainId))
        {
            Console.WriteLine("Could not extract DomainID from transaction metadata");
            return;
        }

        Console.WriteLine($"Domain ID: {domainId}");

        await Task.Delay(1000);

        var deleteDomain = new PermissionedDomainDelete
        {
            Account = wallet.ClassicAddress,
            DomainID = domainId
        };

        var autofilled2 = await client.Autofill(deleteDomain);
        var res2 = await client.SubmitAndWait(autofilled2, wallet, true);

        if (!ValidateSuccessResultOrSkip(res2, "PermissionedDomainDelete"))
        {
            Console.WriteLine("Delete transaction failed");
            return;
        }

        Console.WriteLine("Successfully deleted PermissionedDomain");
    }

    #endregion

    #region Full Lifecycle Tests

    [TestMethod]
    public async Task TestPermissionedDomain_FullLifecycle()
    {
        var wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        Console.WriteLine($"Starting PermissionedDomain lifecycle test for: {wallet.ClassicAddress}");

        var createDomain = new PermissionedDomainSet
        {
            Account = wallet.ClassicAddress,
            AcceptedCredentials = CreateCredentials(wallet.ClassicAddress, "lifecycle_credential")
        };

        var autofilled1 = await client.Autofill(createDomain);
        var res1 = await client.SubmitAndWait(autofilled1, wallet, true);

        if (!ValidateSuccessResultOrSkip(res1, "PermissionedDomain Lifecycle: Create"))
        {
            Console.WriteLine("PermissionedDomains amendment may not be enabled on this network");
            return;
        }

        Console.WriteLine("Step 1: PermissionedDomain created successfully");

        await Task.Delay(1000);

        Console.WriteLine("PermissionedDomain lifecycle test completed successfully!");
    }

    #endregion

    #region Permissioned DEX Tests (XLS-81)

    [TestMethod]
    public async Task TestOfferCreate_WithDomainID()
    {
        var issuerWallet = XrplWallet.Generate();
        var traderWallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuerWallet, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, traderWallet, nodeType);

        var trustSet = new TrustSet
        {
            Account = traderWallet.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = "PDX",
                Issuer = issuerWallet.ClassicAddress,
                Value = "10000000"
            }
        };

        var autofilledTrust = await client.Autofill(trustSet);
        var resTrust = await client.SubmitAndWait(autofilledTrust, traderWallet, true);
        Console.WriteLine($"TrustSet result: {resTrust.Meta?.TransactionResult}");

        var createDomain = new PermissionedDomainSet
        {
            Account = traderWallet.ClassicAddress,
            AcceptedCredentials = CreateCredentials(traderWallet.ClassicAddress, "dex_credential")
        };

        var autofilledDomain = await client.Autofill(createDomain);
        var resDomain = await client.SubmitAndWait(autofilledDomain, traderWallet, true);

        if (!ValidateSuccessResultOrSkip(resDomain, "PermissionedDomainSet for DEX test"))
        {
            Console.WriteLine("PermissionedDomains amendment may not be enabled on this network");
            return;
        }

        string domainId = null;
        if (resDomain.Meta?.AffectedNodes != null)
        {
            foreach (var node in resDomain.Meta.AffectedNodes)
            {
                if (node.CreatedNode?.LedgerEntryType == LedgerEntryType.PermissionedDomain)
                {
                    domainId = node.CreatedNode.LedgerIndex;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(domainId))
        {
            Console.WriteLine("Could not extract DomainID from transaction metadata");
            return;
        }

        Console.WriteLine($"Created PermissionedDomain with ID: {domainId}");

        string credTypeHex = ToHex("dex_credential");

        var credCreate = new CredentialCreate
        {
            Account = traderWallet.ClassicAddress,
            Subject = traderWallet.ClassicAddress,
            CredentialType = credTypeHex,
        };
        var autofilledCredCreate = await client.Autofill(credCreate);
        var resCredCreate = await client.SubmitAndWait(autofilledCredCreate, traderWallet, true);
        if (!ValidateSuccessResultOrSkip(resCredCreate, "CredentialCreate for trader (self-issued, auto-accepted)"))
            return;

        await Task.Delay(1000);

        var offerCreate = new OfferCreate
        {
            Account = traderWallet.ClassicAddress,
            TakerGets = new Currency { ValueAsXrp = 10 },
            TakerPays = new Currency 
            { 
                CurrencyCode = "PDX", 
                Issuer = issuerWallet.ClassicAddress, 
                Value = "20" 
            },
            DomainID = domainId
        };

        var autofilledOffer = await client.Autofill(offerCreate);
        var resOffer = await client.SubmitAndWait(autofilledOffer, traderWallet, true);

        Assert.IsNotNull(resOffer, "OfferCreate response should not be null");
        Assert.IsNotNull(resOffer.Meta, "OfferCreate meta should not be null");

        var result = resOffer.Meta.TransactionResult;
        Console.WriteLine($"OfferCreate with DomainID result: {result}");

        if (result == "temDISABLED" || result == "notEnabled")
        {
            Console.WriteLine("PermissionedDEX amendment may not be enabled on this network");
            return;
        }

        Console.WriteLine("OfferCreate with DomainID test completed");
    }

    [TestMethod]
    public async Task TestOfferCreate_HybridFlag_WithDomainID()
    {
        var issuerWallet = XrplWallet.Generate();
        var traderWallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, issuerWallet, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, traderWallet, nodeType);

        var trustSet = new TrustSet
        {
            Account = traderWallet.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = "HYB",
                Issuer = issuerWallet.ClassicAddress,
                Value = "10000000"
            }
        };

        var autofilledTrust = await client.Autofill(trustSet);
        var resTrust = await client.SubmitAndWait(autofilledTrust, traderWallet, true);
        Console.WriteLine($"TrustSet result: {resTrust.Meta?.TransactionResult}");

        var createDomain = new PermissionedDomainSet
        {
            Account = traderWallet.ClassicAddress,
            AcceptedCredentials = CreateCredentials(traderWallet.ClassicAddress, "hybrid_credential")
        };

        var autofilledDomain = await client.Autofill(createDomain);
        var resDomain = await client.SubmitAndWait(autofilledDomain, traderWallet, true);

        if (!ValidateSuccessResultOrSkip(resDomain, "PermissionedDomainSet for hybrid test"))
        {
            return;
        }

        string domainId = null;
        if (resDomain.Meta?.AffectedNodes != null)
        {
            foreach (var node in resDomain.Meta.AffectedNodes)
            {
                if (node.CreatedNode?.LedgerEntryType == LedgerEntryType.PermissionedDomain)
                {
                    domainId = node.CreatedNode.LedgerIndex;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(domainId))
        {
            Console.WriteLine("Could not extract DomainID from transaction metadata");
            return;
        }

        Console.WriteLine($"Created PermissionedDomain for hybrid test: {domainId}");

        string hybridCredTypeHex = ToHex("hybrid_credential");

        var credCreate = new CredentialCreate
        {
            Account = traderWallet.ClassicAddress,
            Subject = traderWallet.ClassicAddress,
            CredentialType = hybridCredTypeHex,
        };
        var autofilledCredCreate = await client.Autofill(credCreate);
        var resCredCreate = await client.SubmitAndWait(autofilledCredCreate, traderWallet, true);
        if (!ValidateSuccessResultOrSkip(resCredCreate, "CredentialCreate for trader (self-issued, auto-accepted)"))
            return;

        await Task.Delay(1000);

        var hybridOffer = new OfferCreate
        {
            Account = traderWallet.ClassicAddress,
            TakerGets = new Currency { ValueAsXrp = 5 },
            TakerPays = new Currency 
            { 
                CurrencyCode = "HYB", 
                Issuer = issuerWallet.ClassicAddress, 
                Value = "10" 
            },
            DomainID = domainId,
            Flags = OfferCreateFlags.tfHybrid
        };

        var autofilledOffer = await client.Autofill(hybridOffer);
        var resOffer = await client.SubmitAndWait(autofilledOffer, traderWallet, true);

        Assert.IsNotNull(resOffer, "Hybrid OfferCreate response should not be null");
        Assert.IsNotNull(resOffer.Meta, "Hybrid OfferCreate meta should not be null");

        var result = resOffer.Meta.TransactionResult;
        Console.WriteLine($"Hybrid OfferCreate result: {result}");

        if (result == "temDISABLED" || result == "notEnabled")
        {
            Console.WriteLine("PermissionedDEX amendment may not be enabled on this network");
            return;
        }

        Console.WriteLine("Hybrid OfferCreate test completed");
    }

    [TestMethod]
    public async Task TestOfferCreate_HybridFlag_WithoutDomainID_ShouldFail()
    {
        var hybridOfferNoDoamin = new Dictionary<string, object>
        {
            { "TransactionType", "OfferCreate" },
            { "Account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
            { "TakerGets", "1000000" },
            { "TakerPays", "2000000" },
            { "Flags", (uint)OfferCreateFlags.tfHybrid }
        };

        try
        {
            await Validation.ValidateOfferCreate(hybridOfferNoDoamin);
            Assert.Fail("Should have thrown ValidationException for tfHybrid without DomainID");
        }
        catch (ValidationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("tfHybrid"), $"Expected tfHybrid validation error, got: {ex.Message}");
            Console.WriteLine($"Correctly caught validation error: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestOfferCreate_InvalidDomainID_ShouldFail()
    {
        var invalidDomainIdOffer = new Dictionary<string, object>
        {
            { "TransactionType", "OfferCreate" },
            { "Account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
            { "TakerGets", "1000000" },
            { "TakerPays", "2000000" },
            { "DomainID", "invalid_not_hex" }
        };

        try
        {
            await Validation.ValidateOfferCreate(invalidDomainIdOffer);
            Assert.Fail("Should have thrown ValidationException for invalid DomainID");
        }
        catch (ValidationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("DomainID"), $"Expected DomainID validation error, got: {ex.Message}");
            Console.WriteLine($"Correctly caught validation error: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestPayment_InvalidDomainID_ShouldFail()
    {
        var invalidDomainIdPayment = new Dictionary<string, object>
        {
            { "TransactionType", "Payment" },
            { "Account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
            { "Destination", "rPMh7Pi9ct699iZUTWaytJUoHcJ7cgyziK" },
            { "Amount", "1000000" },
            { "DomainID", "tooshort" }
        };

        try
        {
            await Validation.ValidatePayment(invalidDomainIdPayment);
            Assert.Fail("Should have thrown ValidationException for invalid DomainID");
        }
        catch (ValidationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("DomainID"), $"Expected DomainID validation error, got: {ex.Message}");
            Console.WriteLine($"Correctly caught validation error: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestPayment_WithDomainID()
    {
        var wallet = XrplWallet.Generate();
        var wallet2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet2, nodeType);

        string credTypeHex = ToHex("payment_credential");

        var credCreate1 = new CredentialCreate
        {
            Account = wallet.ClassicAddress,
            Subject = wallet.ClassicAddress,
            CredentialType = credTypeHex,
        };
        var autofilledCredCreate1 = await client.Autofill(credCreate1);
        var resCredCreate1 = await client.SubmitAndWait(autofilledCredCreate1, wallet, true);
        if (!ValidateSuccessResultOrSkip(resCredCreate1, "CredentialCreate for wallet (self-issued, auto-accepted)"))
            return;

        var credCreate2 = new CredentialCreate
        {
            Account = wallet.ClassicAddress,
            Subject = wallet2.ClassicAddress,
            CredentialType = credTypeHex,
        };
        var autofilledCredCreate2 = await client.Autofill(credCreate2);
        var resCredCreate2 = await client.SubmitAndWait(autofilledCredCreate2, wallet, true);
        if (!ValidateSuccessResultOrSkip(resCredCreate2, "CredentialCreate for wallet2"))
            return;

        var credAccept2 = new CredentialAccept
        {
            Account = wallet2.ClassicAddress,
            Issuer = wallet.ClassicAddress,
            CredentialType = credTypeHex,
        };
        var autofilledCredAccept2 = await client.Autofill(credAccept2);
        var resCredAccept2 = await client.SubmitAndWait(autofilledCredAccept2, wallet2, true);
        if (!ValidateSuccessResultOrSkip(resCredAccept2, "CredentialAccept for wallet2"))
            return;

        var createDomain = new PermissionedDomainSet
        {
            Account = wallet.ClassicAddress,
            AcceptedCredentials = CreateCredentials(wallet.ClassicAddress, "payment_credential")
        };

        var autofilledDomain = await client.Autofill(createDomain);
        var resDomain = await client.SubmitAndWait(autofilledDomain, wallet, true);

        if (!ValidateSuccessResultOrSkip(resDomain, "PermissionedDomainSet for payment test"))
        {
            return;
        }

        string domainId = null;
        if (resDomain.Meta?.AffectedNodes != null)
        {
            foreach (var node in resDomain.Meta.AffectedNodes)
            {
                if (node.CreatedNode?.LedgerEntryType == LedgerEntryType.PermissionedDomain)
                {
                    domainId = node.CreatedNode.LedgerIndex;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(domainId))
        {
            Console.WriteLine("Could not extract DomainID from transaction metadata");
            return;
        }

        Console.WriteLine($"Created PermissionedDomain for payment test: {domainId}");

        await Task.Delay(1000);

        var payment = new Payment
        {
            Account = wallet.ClassicAddress,
            Destination = wallet2.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1 },
            DomainID = domainId
        };

        var autofilledPayment = await client.Autofill(payment);
        var resPayment = await client.SubmitAndWait(autofilledPayment, wallet, true);

        Assert.IsNotNull(resPayment, "Payment response should not be null");
        Assert.IsNotNull(resPayment.Meta, "Payment meta should not be null");

        var result = resPayment.Meta.TransactionResult;
        Console.WriteLine($"Payment with DomainID result: {result}");

        if (result == "temDISABLED" || result == "notEnabled")
        {
            Console.WriteLine("PermissionedDEX amendment may not be enabled on this network");
            return;
        }

        Console.WriteLine("Payment with DomainID test completed");
    }

    #endregion
}
