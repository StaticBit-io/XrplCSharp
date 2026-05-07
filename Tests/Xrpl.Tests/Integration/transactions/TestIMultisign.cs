using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[DoNotParallelize]
public class TestIMultisign
{
    public TestContext TestContext { get; set; }
    public static SetupIntegration runner;

    static XrplWallet walletMultiSign = XrplWallet.FromNormalizedText("multisign payment test account");
    static XrplWallet walletMultiSigner_1 = XrplWallet.FromNormalizedText("multisign payment signer 1");
    static XrplWallet walletMultiSigner_2 = XrplWallet.FromNormalizedText("multisign payment signer 2");
    static XrplWallet walletDestination = XrplWallet.FromNormalizedText("multisign payment destination");

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        await TryFillAccounts(walletMultiSign, walletMultiSigner_1, walletMultiSigner_2, walletDestination);
        if (!await SetSigners(walletMultiSign, walletMultiSigner_1, walletMultiSigner_2))
        {
            throw new RippledNotInitializedException();
        }

        if (!await DisableMaster(walletMultiSign))
        {
            throw new RippledNotInitializedException();
        }
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        runner.client.Dispose();
    }

    #region PaymentMultiSign

    [TestMethod]
    public async Task TestIMultisign_Payment_SubmitMulti()
    {
        var tx = await GetPaymentForMultiSign();

        var res = await runner.client.SubmitMulti(tx, new List<XrplWallet>() { walletMultiSigner_1, walletMultiSigner_2 }, true);

        ValidateResult(res);
    }

    [TestMethod]
    public async Task TestIMultisign_Payment_ManualSigningV2()
    {
        var tx = await GetPaymentForMultiSign();

        var sig1 = walletMultiSigner_1.Sign(tx, true);
        var stx1 = sig1.GetTx();
        var sig2 = walletMultiSigner_2.Sign(stx1, true);
        var res = await runner.client.SubmitRequest(sig2.TxBlob, true);

        ValidateResult(res);
    }

    [TestMethod]
    public async Task TestIMultisign_Payment_ManualSigningV3_CombineBlobs()
    {
        var tx = await GetPaymentForMultiSign();

        var sig1 = walletMultiSigner_1.Sign(tx, true);
        var sig2 = walletMultiSigner_2.Sign(tx, true);
        var combined = Signer.Multisign(new[] { sig1.TxBlob, sig2.TxBlob });
        var res = await runner.client.SubmitRequest(combined, true);

        ValidateResult(res);
    }

    [TestMethod]
    public async Task TestIMultisign_Payment_DuplicateSigners_AreDeduplicated()
    {
        var tx = await GetPaymentForMultiSign();

        var sig1 = walletMultiSigner_1.Sign(tx, true);
        var sig2 = walletMultiSigner_2.Sign(tx, true);
        var sig1_dup = walletMultiSigner_1.Sign(tx, true);
        var combined = Signer.Multisign(new[] { sig1.TxBlob, sig2.TxBlob, sig1_dup.TxBlob });
        var res = await runner.client.SubmitRequest(combined, true);

        ValidateResult(res);
    }

    private static async Task<Payment> GetPaymentForMultiSign()
    {
        var owner = walletMultiSign;

        var request = new AccountInfoRequest(owner.ClassicAddress);
        var accountInfo = await runner.client.AccountInfo(request);

        var payment = new Payment
        {
            Account = owner.ClassicAddress,
            Sequence = accountInfo.AccountData.Sequence,
            Amount = new Currency
            {
                ValueAsXrp = 1m,
            },
            Destination = walletDestination.ClassicAddress,
            Fee = new Currency { Value = "30" }
        };

        payment = await runner.client.Autofill(payment);
        return payment;
    }

    #endregion

    #region NegativeTests

    [TestMethod]
    public async Task TestIMultisign_InsufficientSigners_Fails()
    {
        var tx = await GetPaymentForMultiSign();

        var res = await runner.client.SubmitMulti(tx, new List<XrplWallet>() { walletMultiSigner_1 }, true);

        Assert.AreNotEqual("tesSUCCESS", res.EngineResult, "Transaction should fail with insufficient signers");
    }

    #endregion

    #region Helpers

    private static void ValidateResult(Submit res)
    {
        if (res is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
        {
            throw new RippleException($"Invalid result, {res.EngineResult}");
        }
    }

    private static async Task<bool> SetSigners(XrplWallet owner, XrplWallet signer1, XrplWallet signer2)
    {
        var acc = await runner.client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress) { SignerLists = true });
        if (acc.SignerLists is { Length: > 0 })
        {
            return true;
        }
        var sls = new SignerListSet
        {
            Account = owner.ClassicAddress,
            SignerQuorum = 2,
            SignerEntries = new()
            {
                new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = signer1.ClassicAddress, SignerWeight = 1 } },
                new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = signer2.ClassicAddress, SignerWeight = 1, } },
            },
            Fee = new Currency { Value = "15" },
            Sequence = acc.AccountData.Sequence,
        };

        var res = await runner.client.SubmitAndWait(sls, owner, true, true);
        if (res.Meta.TransactionResult == "tesSUCCESS")
        {
            return true;
        }

        return false;
    }

    private static async Task<bool> DisableMaster(XrplWallet owner)
    {
        var acc = await runner.client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress));
        if (acc.AccountFlags.DisableMasterKey)
        {
            return true;
        }
        var disableMaster = new AccountSet
        {
            Account = owner.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDisableMaster,
            Fee = new Currency { Value = "15" },
            Sequence = acc.AccountData.Sequence,
        };
        var res = await runner.client.SubmitAndWait(disableMaster, owner, true);
        if (res.Meta.TransactionResult == "tesSUCCESS")
        {
            return true;
        }

        return false;
    }

    private static async Task TryFillAccounts(params XrplWallet[] wallets)
    {
        foreach (var xrplWallet in wallets)
        {
            try
            {
                var info = await runner.client.GetXrpFreeBalance(xrplWallet.ClassicAddress);
                Console.WriteLine($"Balance {xrplWallet.ClassicAddress} - {info} XRP");

                if (info <= 10)
                {
                    await Utils.FundAccount(runner.client, xrplWallet);
                    Console.WriteLine($"Fund {xrplWallet.ClassicAddress}");
                }
                continue;
            }
            catch (Exception e)
            {
            }
            await Utils.FundAccount(runner.client, xrplWallet);
            Console.WriteLine($"Fund {xrplWallet.ClassicAddress}");
        }
    }

    #endregion
}