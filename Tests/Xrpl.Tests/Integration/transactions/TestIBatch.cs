using Microsoft.VisualStudio.TestTools.UnitTesting;


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Sugar;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using Xrpl.Client;
namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[DoNotParallelize]
public class TestIBatch
{
    private static bool batchV11Active;

    // private static int Timeout = 20;
    public TestContext TestContext { get; set; }
    public static SetupIntegration runner;

    // Generated per run, never derived from a fixed phrase. A phrase is the same account on
    // every network, so on a public one it is shared with everyone who ever ran this test:
    // the state it starts from is whatever they left, and the setup below disables a master
    // key on it. On the standalone stand a derived account also carried state between runs,
    // which let a "create" test quietly pass as a modify.
    static XrplWallet walletPrimary = XrplWallet.Generate();
    static XrplWallet walletSecondary_1 = XrplWallet.Generate();
    static XrplWallet walletSecondary_2 = XrplWallet.Generate();
    static XrplWallet walletMultiSign = XrplWallet.Generate();
    static XrplWallet walletMultiSigner_1 = XrplWallet.Generate();
    static XrplWallet walletMultiSigner_2 = XrplWallet.Generate();
    static XrplWallet walletRegularKey = XrplWallet.Generate();
    static XrplWallet walletRegularKey_signer = XrplWallet.Generate();

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);

        batchV11Active = await AmendmentGuard.IsEnabledAsync(runner.client, AmendmentGuard.BatchV11);
        if (!batchV11Active)
        {
            // Tests will be reported as inconclusive from CheckBatchV11Amendment.
            return;
        }

        await TryFillAccounts(walletPrimary, walletSecondary_1, walletSecondary_2, walletMultiSign, walletMultiSigner_1, walletMultiSigner_2, walletRegularKey, walletRegularKey_signer);
        if (!await SetSigners(walletMultiSign, walletMultiSigner_1, walletMultiSigner_2))
        {
            throw new RippledNotInitializedException();
        }

        if (!await DisableMaster(walletMultiSign))
        {
            throw new RippledNotInitializedException();
        }
    }

    [TestInitialize]
    public void CheckBatchV11Amendment()
    {
        if (!batchV11Active)
        {
            Assert.Inconclusive("BatchV1_1 amendment is not enabled on the test node; start .ci-config/docker-compose.batchv11.yml to run these tests.");
        }
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        runner.client.Dispose();
    }

    #region SingleAccountBatchWithMultiSign

    [TestMethod]
    public async Task TestBatchSingleMultiSignV1()
    {
        var tx = await GetTxForSingleMultiSign();

        // sign and submit the transaction
        // #v1
        var res = await runner.client.SubmitMulti(tx, new List<XrplWallet>() { walletMultiSigner_1, walletMultiSigner_2 }, true);

        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchSingleMultiSignV2()
    {
        var tx = await GetTxForSingleMultiSign();
        //#v2
        var sig1 = walletMultiSigner_1.Sign(tx, true);
        var stx1 = sig1.GetTx();
        var sig2 = walletMultiSigner_2.Sign(stx1, true);
        var stx2 = sig2.GetTx();
        var res = await runner.client.SubmitRequest(sig2.TxBlob, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchSingleMultiSignV3()
    {
        var tx = await GetTxForSingleMultiSign();
        //#v3
        var sig1 = walletMultiSigner_1.Sign(tx, true);
        var stx1 = sig1.GetTx();
        var sig2 = walletMultiSigner_2.Sign(stx1, true);
        var singed = Signer.Multisign([sig1.TxBlob, sig2.TxBlob]);
        var res = await runner.client.SubmitRequest(singed, true);
        await ValidateResultAsync(res);
    }

    private static async Task<Batch> GetTxForSingleMultiSign()
    {
        var owner = walletMultiSign;
        Console.WriteLine("NEXT");

        var request = new AccountInfoRequest(owner.ClassicAddress);
        var accountInfo = await runner.client.AccountInfo(request).Typed();

        //var flags = BatchGlobalFlags.tfInnerBatchTxn;
        // Внутренний Payment #1
        var payment1 = new Payment
        {
            Sequence = accountInfo.AccountData.Sequence + 1,
            Account = owner.ClassicAddress,
            Amount = new Currency
            {
                ValueAsXrp = 1m,
            },
            Destination = walletSecondary_1.ClassicAddress,
        }.ToBatchTx();

        // Внутренний Payment #2
        var payment2 = new Payment
        {
            Sequence = accountInfo.AccountData.Sequence + 2,
            Account = owner.ClassicAddress,
            Amount = new Currency
            {
                ValueAsXrp = 1m,
            },
            Destination = walletSecondary_2.ClassicAddress,
        }.ToBatchTx();

        // Собираем внешний Batch
        var tx = new Batch
        {
            Account = owner.ClassicAddress,
            Sequence = accountInfo.AccountData.Sequence,
            Flags = BatchFlags.tfAllOrNothing, // режим: или все выполняются, или ни одна
            RawTransactions = new List<RawTransactionWrapper>
            {
                payment1,
                payment2,
            },
            Fee = 70
        };

        tx = await runner.client.Autofill(tx, 2);
        return tx;
    }

    #endregion

    #region BatchMultiAccounts

    [TestMethod]
    public async Task TestBatchMultiAccounts_V1()
    {
        var batch = await GetTxForBatchMultiAccounts();

        //#v1
        var res = await runner.client.SubmitMultiBatch(batch, new[] { walletPrimary, walletSecondary_1, walletSecondary_2 }, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccounts_V2_1()
    {
        var batch = await GetTxForBatchMultiAccounts();

        //#v2
        var sig1 = walletSecondary_2.Sign(batch);
        var sig2 = walletSecondary_1.Sign(batch);
        var combined = XrplWallet.CombineBatchSigners(new[] { sig1.TxBlob, sig2.TxBlob});
        var sig3 = walletPrimary.Sign(combined.GetTx());
        var res = await runner.client.SubmitRequest(sig3.TxBlob, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccounts_V2_2()
    {
        var batch = await GetTxForBatchMultiAccounts();

        //#v2
        var sig1 = walletPrimary.Sign(batch);
        var sig2 = walletSecondary_1.Sign(batch);
        var sig3 = walletSecondary_2.Sign(batch);
        var combined = XrplWallet.CombineBatchSigners(new[] { sig1.TxBlob, sig2.TxBlob,sig3.TxBlob });
        var res = await runner.client.SubmitRequest(combined.TxBlob, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccounts_V3()
    {
        var batch = await GetTxForBatchMultiAccounts();

        //#v2
        var sig1 = walletSecondary_1.Sign(batch);
        var sig2 = walletSecondary_2.Sign(sig1.GetTx());
        var sig3 = walletPrimary.Sign(sig2.GetTx());
        var res = await runner.client.SubmitRequest(sig3.TxBlob, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccounts_V3_2()
    {
        var batch = await GetTxForBatchMultiAccounts();

        //#v2
        var sig1 = walletPrimary.Sign(batch);
        var sig2 = walletSecondary_2.Sign(sig1.GetTx());
        var sig3 = walletSecondary_1.Sign(sig2.GetTx());
        var tx = sig3.GetTxDictionary();
        var res = await runner.client.SubmitRequest(sig3.TxBlob, true);
        await ValidateResultAsync(res);
    }

    private static async Task<Batch> GetTxForBatchMultiAccounts()
    {
        // Внутренний #1 — от w1 (seq = next для w1)
        var p1 = new Payment
        {
            Account = walletPrimary.ClassicAddress,
            Destination = walletSecondary_1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.1m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #2 — от w2 (seq = next для w2)
        var p2 = new Payment
        {
            Account = walletSecondary_1.ClassicAddress,
            Destination = walletPrimary.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.2m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #3 — от w3 (seq = next для w3)
        var p3 = new Payment
        {
            Account = walletSecondary_2.ClassicAddress,
            Destination = walletPrimary.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.3m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внешний Batch — платит комиссию w1 (может быть любой плательщик)
        var batch = new Batch
        {
            Account = walletPrimary.ClassicAddress,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = new List<RawTransactionWrapper> { p1, p2, p3 },
            //Fee = new Currency() { Value = "70" }
            // Рекомендуется проставить LLS и Fee (не показано для краткости)
        };
        batch = await runner.client.Autofill(batch);
        return batch;
    }


    #endregion

    #region MultiAccountBatchWithTopMultiSign

    [TestMethod]
    public async Task TestBatchMultiAccountsWithTopMultiSign_V1()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetMultiAccountBatchWithTopMultiSign();

        var res = await runner.client.SubmitMultiBatch(batch, new[] { w1, w2, owner, signer1, signer2 }, true);

        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccountsWithTopMultiSign_V2()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetMultiAccountBatchWithTopMultiSign();
        var sig1 = w1.Sign(batch);
        var sig2 = w2.Sign(batch);
        var sig3 = signer1.Sign(batch, true);
        var sig4 = signer2.Sign(batch, true);
        var combined = XrplWallet.CombineBatchSigners(sig1.TxBlob,sig2.TxBlob,sig3.TxBlob,sig4.TxBlob);
        var res = await runner.client.SubmitRequest(combined.TxBlob, true);

        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccountsWithTopMultiSign_V3_1()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetMultiAccountBatchWithTopMultiSign();
        var sig3 = signer1.Sign(batch, true);
        var sig4 = signer2.Sign(sig3.GetTx(), true);
        var sig1 = w1.Sign(sig4.GetTx());
        var sig2 = w2.Sign(sig1.GetTx());
        var res = await runner.client.SubmitRequest(sig2.TxBlob, true);

        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccountsWithTopMultiSign_V3_2()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetMultiAccountBatchWithTopMultiSign();
        var sig1 = w1.Sign(batch);
        var sig2 = w2.Sign(sig1.GetTx());
        var sig3 = signer1.Sign(sig2.GetTx(), true);
        var sig4 = signer2.Sign(sig3.GetTx(), true);
        var res = await runner.client.SubmitRequest(sig4.TxBlob, true);

        await ValidateResultAsync(res);
    }

    private static async Task<(XrplWallet owner, XrplWallet signer1, XrplWallet signer2, XrplWallet w1, XrplWallet w2, Batch batch)> GetMultiAccountBatchWithTopMultiSign()
    {
        // Владелец мультисиг-аккаунта
        var owner = walletMultiSign;
        // Подписанты (могут быть любые аккаунты/ключи)
        var signer1 = walletMultiSigner_1;
        var signer2 = walletMultiSigner_2;

        var w1 = walletSecondary_1;
        var w2 = walletSecondary_2;

        // Внутренний #1 — от w1 (seq = next для w1)
        var p1 = new Payment
        {
            Account = w1.ClassicAddress,
            Destination = w2.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.1m },
        }.ToBatchTx();

        // Внутренний #2 — от w2 (seq = next для w2)
        var p2 = new Payment
        {
            Account = w2.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.2m },
        }.ToBatchTx();

        // Внутренний #3 — от w3 (seq = next для w3)
        var p3 = new Payment
        {
            Account = owner.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.3m },
        }.ToBatchTx();

        // Внешний Batch — корневой аккаунт = owner (мультисиг)
        var batch = new Batch
        {
            Account = owner.ClassicAddress,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = new List<RawTransactionWrapper> { p1, p2, p3 },
            // Рекомендуется проставить LLS и Fee (не показано для краткости)
        };
        batch = await runner.client.Autofill(batch,4);
        return (owner, signer1, signer2, w1, w2, batch);
    }

    #endregion


    #region MultiAccountBatchWithInnerMultiSign

    [TestMethod]
    public async Task TestBatchMultiAccountsWithInnerMultiSignV1()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetBatchMultiAccountsWithInnerMultiSign();

        var res = await runner.client.SubmitMultiBatch(batch, new[] { w1, w2, owner, signer1, signer2 }, true);
        await ValidateResultAsync(res);
    }


    [TestMethod]
    public async Task TestBatchMultiAccountsWithInnerMultiSignV2()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetBatchMultiAccountsWithInnerMultiSign();
        var sig1 = w1.Sign(batch);
        var sig2 = w2.Sign(batch);
        var sig3 = signer1.Sign(batch, true, owner.ClassicAddress);
        var sig4 = signer2.Sign(batch, true, owner.ClassicAddress);
        var combined = XrplWallet.CombineBatchSigners(sig1.TxBlob, sig2.TxBlob, sig3.TxBlob, sig4.TxBlob);
        var res = await runner.client.SubmitRequest(combined.TxBlob, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccountsWithInnerMultiSignV3_1()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetBatchMultiAccountsWithInnerMultiSign();
        var sig3 = signer1.Sign(batch, true, owner.ClassicAddress);
        var sig4 = signer2.Sign(sig3.GetTx(), true, owner.ClassicAddress);
        var sig1 = w1.Sign(sig4.GetTx());
        var sig2 = w2.Sign(sig1.GetTx());
        var json = sig2.GetTx().ToJson();
        var res = await runner.client.SubmitRequest(sig2.TxBlob, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiAccountsWithInnerMultiSignV3_2()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetBatchMultiAccountsWithInnerMultiSign();
        var sig1 = w1.Sign(batch);
        var sig2 = w2.Sign(sig1.GetTx());
        var sig3 = signer1.Sign(sig2.GetTx(), true, owner.ClassicAddress);
        var sig4 = signer2.Sign(sig3.GetTx(), true, owner.ClassicAddress);
        var res = await runner.client.SubmitRequest(sig4.TxBlob, true);

        await ValidateResultAsync(res);
    }



    private static async Task<(XrplWallet owner, XrplWallet signer1, XrplWallet signer2, XrplWallet w1, XrplWallet w2, Batch batch)> GetBatchMultiAccountsWithInnerMultiSign()
    {
        // Владелец мультисиг-аккаунта
        var owner = walletMultiSign;
        // Подписанты (могут быть любые аккаунты/ключи)
        var signer1 = walletMultiSigner_1;
        var signer2 = walletMultiSigner_2;

        var w1 = walletSecondary_1;
        var w2 = walletSecondary_2;

        // Внутренний #1 — от w1 (seq = next для w1)
        var p1 = new Payment
        {
            Account = w1.ClassicAddress,
            Destination = w2.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.1m },
        }.ToBatchTx();

        // Внутренний #2 — от w2 (seq = next для w2)
        var p2 = new Payment
        {
            Account = w2.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.2m },
        }.ToBatchTx();

        // Внутренний #3 — от w3 (seq = next для w3)
        var p3 = new Payment
        {
            Account = owner.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.3m },
        }.ToBatchTx();

        // Внешний Batch — платит комиссию w1 (может быть любой плательщик)
        var batch = new Batch
        {
            Account = w1.ClassicAddress,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = new List<RawTransactionWrapper> { p1, p2, p3 },
        };
        batch = await runner.client.Autofill(batch,2);
        return (owner, signer1, signer2, w1, w2, batch);
    }

    #endregion

    #region DuplicateSignaturesTests

    [TestMethod]
    public async Task TestBatchWithDuplicateSignatures_AreDeduplicated()
    {
        var batch = await GetTxForBatchMultiAccounts();

        var sig1 = walletPrimary.Sign(batch);
        var sig2 = walletSecondary_1.Sign(batch);
        var sig3 = walletSecondary_2.Sign(batch);
        var sig1_dup = walletPrimary.Sign(batch);
        var sig2_dup = walletSecondary_1.Sign(batch);

        var combined = XrplWallet.CombineBatchSigners(new[] { sig1.TxBlob, sig2.TxBlob, sig3.TxBlob, sig1_dup.TxBlob, sig2_dup.TxBlob });
        var res = await runner.client.SubmitRequest(combined.TxBlob, true);
        await ValidateResultAsync(res);
    }

    [TestMethod]
    public async Task TestBatchMultiSign_WithDuplicateSignatures_AreDeduplicated()
    {
        var (owner, signer1, signer2, w1, w2, batch) = await GetMultiAccountBatchWithTopMultiSign();

        var sig1 = w1.Sign(batch);
        var sig2 = w2.Sign(batch);
        var sig3 = signer1.Sign(batch, true);
        var sig4 = signer2.Sign(batch, true);
        var sig3_dup = signer1.Sign(batch, true);

        var combined = XrplWallet.CombineBatchSigners(sig1.TxBlob, sig2.TxBlob, sig3.TxBlob, sig4.TxBlob, sig3_dup.TxBlob);
        var res = await runner.client.SubmitRequest(combined.TxBlob, true);

        await ValidateResultAsync(res);
    }

    #endregion

    #region NegativeTests

    [TestMethod]
    public async Task TestBatchMultiAccounts_NoSigners_Fails()
    {
        var batch = await GetTxForBatchMultiAccounts();

        await Helper.ThrowsExceptionAsync<ValidationException>(async () =>
        {
            await runner.client.SubmitMultiBatch(batch, Array.Empty<XrplWallet>(), true);
        });
    }

    #endregion

    /// <summary>
    /// Checks the engine result, then waits until the transaction is in a ledger and checks the
    /// result the ledger recorded.
    /// </summary>
    /// <remarks>
    /// An engine result is provisional: it says what one node made of the transaction against
    /// its open ledger, not what the network settled on, and <c>terQUEUED</c> says it has not
    /// been applied at all. Asserting on it alone let these tests pass on a batch that never
    /// reached a ledger, and it left the account sequences unsettled - the next test autofilled
    /// against a ledger that did not yet carry the previous submission and got
    /// <c>tefPAST_SEQ</c>, which is how this surfaced when the class was first run against
    /// devnet instead of the standalone stand.
    /// </remarks>
    private static async Task ValidateResultAsync(Submit res)
    {
        if (res is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
        {
            throw new RippleException($"Invalid result, {res.EngineResult}");
        }

        string hash = HashLedger.HashSignedTx(res.TxBlob);
        const int MaxAttempts = 15;
        for (int attempt = 1; ; attempt++)
        {
            string ledgerResult = null;
            try
            {
                // Metadata is written when the transaction is applied in a ledger, so its
                // presence is what separates a settled result from the provisional one above
                TransactionResponse tx = await runner.client.TxV1(new TxRequest(hash)).Typed();
                ledgerResult = tx.Meta?.TransactionResult;
            }
            catch (RippledException ex) when (ex.Message.Contains("txnNotFound"))
            {
                // Submitted, not yet in a ledger this node will answer for
            }

            if (ledgerResult is not null)
            {
                if (ledgerResult != "tesSUCCESS")
                {
                    throw new RippleException($"Invalid result in the ledger, {ledgerResult}");
                }
                return;
            }

            if (attempt == MaxAttempts)
            {
                throw new RippleException($"Transaction {hash} did not reach a ledger after {MaxAttempts} lookups.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }



    #region Issue316

    // PREIMAGE-SHA-256 condition over the empty preimage and its 4-byte fulfillment.
    private const string EmptyPreimageCondition = "A0258020E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855810100";
    private const string EmptyPreimageFulfillment = "A0028000";

    /// <summary>
    /// rippled builds the batch preimage from getSeqProxy().value(): the ticket number when the
    /// outer Sequence is 0. A BatchSigner signature made over 0 instead fails with
    /// "invalidTransaction — Invalid signature".
    /// </summary>
    [TestMethod]
    public async Task TestBatchMultiAccounts_OuterOnTicket()
    {
        uint ticket = await CreateTicketAsync(walletPrimary);

        var batch = new Batch
        {
            Account = walletPrimary.ClassicAddress,
            TicketSequence = ticket,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = new List<RawTransactionWrapper>
            {
                new Payment
                {
                    Account = walletPrimary.ClassicAddress,
                    Destination = walletSecondary_1.ClassicAddress,
                    Amount = new Currency { ValueAsXrp = 1.1m },
                }.ToBatchTx(),
                new Payment
                {
                    Account = walletSecondary_1.ClassicAddress,
                    Destination = walletPrimary.ClassicAddress,
                    Amount = new Currency { ValueAsXrp = 1.2m },
                }.ToBatchTx(),
            },
        };
        batch = await runner.client.Autofill(batch);
        Assert.AreEqual(0u, batch.Sequence, "A ticketed outer carries Sequence 0.");

        var cosigned = walletSecondary_1.Sign(batch);
        var signed = walletPrimary.Sign(cosigned.GetTx());
        var res = await runner.client.SubmitRequest(signed.TxBlob, true);
        await ValidateResultAsync(res);
    }

    /// <summary>
    /// Each inner is priced by its own transactor: an EscrowFinish with a fulfillment costs
    /// 33 base fees on its own, which the old flat one-base-fee-per-inner rule underpriced.
    /// </summary>
    [TestMethod]
    public async Task TestBatchIndependent_EscrowFinishWithFulfillment_AutofilledFeeSuffices()
    {
        const int InnerCount = 8;
        var owner = walletSecondary_2;

        var info = await runner.client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress)
        {
            LedgerIndex = new LedgerIndex(LedgerIndexType.Current)
        }).Typed();
        uint outerSequence = info.AccountData.Sequence.Value;

        var creates = new List<RawTransactionWrapper>();
        for (uint i = 1; i <= InnerCount; i++)
        {
            creates.Add(new EscrowCreate
            {
                Account = owner.ClassicAddress,
                Sequence = outerSequence + i,
                Destination = walletPrimary.ClassicAddress,
                Amount = new Currency { ValueAsXrp = 0.1m },
                Condition = EmptyPreimageCondition,
                CancelAfter = DateTime.UtcNow.AddDays(1),
            }.ToBatchTx());
        }
        var createBatch = await runner.client.Autofill(new Batch
        {
            Account = owner.ClassicAddress,
            Sequence = outerSequence,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = creates,
        });
        await ValidateResultAsync(await runner.client.SubmitRequest(owner.Sign(createBatch).TxBlob, true));
        Assert.AreEqual(InnerCount, await CountEscrowsAsync(owner));

        var finishes = new List<RawTransactionWrapper>();
        for (uint i = 1; i <= InnerCount; i++)
        {
            finishes.Add(new EscrowFinish
            {
                Account = owner.ClassicAddress,
                Owner = owner.ClassicAddress,
                OfferSequence = outerSequence + i,
                Condition = EmptyPreimageCondition,
                Fulfillment = EmptyPreimageFulfillment,
            }.ToBatchTx());
        }
        var finishBatch = await runner.client.Autofill(new Batch
        {
            Account = owner.ClassicAddress,
            Flags = BatchFlags.tfIndependent,
            RawTransactions = finishes,
        });
        await ValidateResultAsync(await runner.client.SubmitRequest(owner.Sign(finishBatch).TxBlob, true));

        // tfIndependent leaves the outer tesSUCCESS whatever its inners did, so the effect is
        // what tells whether every EscrowFinish ran.
        Assert.AreEqual(0, await CountEscrowsAsync(owner), "Every EscrowFinish inner must have finished its escrow.");
    }

    private static async Task<uint> CreateTicketAsync(XrplWallet wallet)
    {
        var res = await runner.client.SubmitAndWait(new TicketCreate { Account = wallet.ClassicAddress, TicketCount = 1 }, wallet, true, true);
        Assert.AreEqual("tesSUCCESS", res.Meta.TransactionResult);

        var objects = await runner.client.AccountObjects(new AccountObjectsRequest(wallet.ClassicAddress)
        {
            Type = LedgerEntryType.Ticket,
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated)
        }).Typed();
        uint? ticket = objects.AccountObjectList.OfType<LOTicket>().Select(t => t.TicketSequence).Max();
        return ticket ?? throw new AssertFailedException("TicketCreate left no Ticket.");
    }

    private static async Task<int> CountEscrowsAsync(XrplWallet wallet)
    {
        var objects = await runner.client.AccountObjects(new AccountObjectsRequest(wallet.ClassicAddress)
        {
            Type = LedgerEntryType.Escrow,
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated)
        }).Typed();
        return objects.AccountObjectList.Count;
    }

    #endregion

    #region Signer


    private static async Task<bool> SetSigners(XrplWallet owner, XrplWallet signer1, XrplWallet signer2)
    {
        var acc = await runner.client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress){SignerLists = true}).Typed();
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
                new SignerEntryWrapper{ SignerEntry = new SignerEntry { Account = signer1.ClassicAddress, SignerWeight = 1 }},
                new SignerEntryWrapper{ SignerEntry = new SignerEntry { Account = signer2.ClassicAddress, SignerWeight = 1, }},
            },
            Fee = new Currency { Value = "15" },
            Sequence = acc.AccountData.Sequence,
        };

        var res =  await runner.client.SubmitAndWait(sls, owner, true, true);
        if (res.Meta.TransactionResult == "tesSUCCESS")
        {
            return true;
        }

        return false;
    }

    private static async Task<bool> DisableMaster(XrplWallet owner)
    {
        var acc = await runner.client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress)).Typed();
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

    #endregion

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
                    await XrplTests.Xrpl.ClientLib.Integration.Utils.FundAccount(runner.client, xrplWallet);
                    Console.WriteLine($"Fund {xrplWallet.ClassicAddress}");
                }
                continue;
            }
            catch (Exception e)
            {

            }
            await XrplTests.Xrpl.ClientLib.Integration.Utils.FundAccount(runner.client, xrplWallet);
            Console.WriteLine($"Fund {xrplWallet.ClassicAddress}");
        }

    }

}