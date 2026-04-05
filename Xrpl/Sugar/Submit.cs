using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/sugar/submit.ts

namespace Xrpl.Sugar;

public static class SubmitSugar
{
    private const int LEDGER_CLOSE_TIME = 1000;

    /// <summary>
    /// Submits a signed/unsigned transaction.<br/>
    /// Steps performed on a transaction:<br/>
    /// 1.<br/>
    /// Autofill.<br/>
    /// 2.<br/>
    /// Sign and Encode.<br/>
    /// 3.<br/>
    /// Submit.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode, and submit.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>A promise that contains SubmitResponse</returns>
    public static async Task<Submit> Submit(
        this IXrplClient client,
        Dictionary<string, dynamic> transaction,
        bool autofill = true,
        bool failHard = false,
        XrplWallet wallet = null,
        CancellationToken cancellationToken = default
    )
    {
        var (signedTx, _) = await client.GetSignedTx(transaction, autofill, failHard: false, wallet, cancellationToken);
        return await SubmitRequest(client, signedTx, failHard, cancellationToken);
    }

    /// <summary>
    /// Asynchronously submits a transaction and verifies that it has been included in a
    /// validated ledger(or has errored/will not be included for some reason).
    /// See[Reliable Transaction Submission] (https://xrpl.org/reliable-transaction-submission.html).
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode, and submit.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>A promise that contains TxResponse, that will return when the transaction has been validated.</returns>
    public static Task<TransactionSummary> SubmitAndWait(
        this IXrplClient client,
        ITransactionRequest transaction,
        XrplWallet wallet = null,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default) =>
        SubmitAndWait(client, transaction.ToDictionary(), wallet, autofill, failHard, cancellationToken);
    /// <summary>
    /// Asynchronously submits a transaction and verifies that it has been included in a
    /// validated ledger(or has errored/will not be included for some reason).
    /// See[Reliable Transaction Submission] (https://xrpl.org/reliable-transaction-submission.html).
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode, and submit.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>A promise that contains TxResponse, that will return when the transaction has been validated.</returns>
    public static async Task<TransactionSummary> SubmitAndWait(
        this IXrplClient client,
        Dictionary<string, dynamic> transaction,
        XrplWallet wallet = null,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        var (signedTx, tx) = await client.GetSignedTx(transaction, autofill, failHard, wallet, cancellationToken);
        var lastLedger = GetLastLedgerSequence(tx);
        if (lastLedger == null)
        {
            throw new ValidationException(
                "Transaction must contain a LastLedgerSequence value for reliable submission.");
        }

        var response = await client.SubmitRequest(signedTx, failHard, cancellationToken);
        var txHash = HashLedger.HashSignedTx(signedTx);
        return await WaitForFinalTransactionOutcome(
            client,
            txHash,
            lastLedger,
            response.EngineResult,
            cancellationToken);
    }

    public static async Task<TransactionSummary> SubmitRequestAndWait(this IXrplClient client, object signedTransaction, bool failHard, CancellationToken cancellationToken = default)
    {
        var signedTx = GetTxBlob(signedTransaction);
        var decoded = XrplBinaryCodec.Decode(signedTx).ToString();
        var tx = JObject.Parse(decoded);
        var lastLedger = GetLastLedgerSequence(tx);
        if (lastLedger == null)
        {
            throw new ValidationException(
                "Transaction must contain a LastLedgerSequence value for reliable submission.");
        }

        var response = await client.SubmitRequest(signedTx, failHard, cancellationToken);
        var txHash = HashLedger.HashSignedTx(signedTx);
        return await WaitForFinalTransactionOutcome(
            client,
            txHash,
            lastLedger,
            response.EngineResult,
            cancellationToken);
    }
    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="signedTransaction">signed Transaction</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitRequest(this IXrplClient client, object signedTransaction, bool failHard, CancellationToken cancellationToken = default)
    {
        //todo activate after fix
        //if (!IsSigned(signedTransaction))
        //{
        //    throw new ValidationException("Transaction must be signed");
        //}

        var signedTxEncoded = GetTxBlob(signedTransaction);

        var request = new SubmitRequest
        {
            Command = "submit",
            TxBlob = signedTxEncoded,
            FailHard = failHard,
        };
        var response = await client.GRequest<Submit, SubmitRequest>(request, cancellationToken);
        return response;
    }

    private static string GetTxBlob(object signedTransaction)
    {
        string signedTxEncoded;
        if (signedTransaction is string transaction)
        {
            signedTxEncoded = transaction;
        }
        else if (signedTransaction is SignatureResult { } sg)
        {
            signedTxEncoded = sg.TxBlob;
        }
        else
        {
            signedTxEncoded = XrplBinaryCodec.Encode(signedTransaction);
        }

        return signedTxEncoded;
    }

    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMulti(
        this IXrplClient client,
        ITransactionRequest tx,
        IEnumerable<XrplWallet> wallets,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        var json = tx.ToJson();
        var txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json)
                     ?? throw new ValidationException("Failed to deserialize tx json");
        var response = await SubmitMulti(client, txJson, wallets, autofill, failHard, cancellationToken);
        return response;
    }

    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMulti(
        this IXrplClient client,
        Dictionary<string, dynamic> tx,
        IEnumerable<XrplWallet> wallets,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        if (wallets is null)
        {
            throw new ValidationException("Wallets must be provided when submitting an unsigned transaction");
        }

        var xrplWallets = wallets as XrplWallet[] ?? wallets.ToArray();
        if (autofill)
        {
            tx = await client.Autofill(tx, signersCount: xrplWallets.Length, cancellationToken: cancellationToken);
        }

        var signed = xrplWallets.Select(c => c.Sign(tx, multisign: true).TxBlob).ToArray();
        var combined = Signer.Multisign(signed);
        var txRes = XrplBinaryCodec.Decode(combined);

        var response = await client.SubmitRequest(combined, failHard: false, cancellationToken: cancellationToken);
        return response;
    }

    /// <summary>
    /// Encodes and submits a Batch signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="txJson">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMultiBatch(
        this IXrplClient client,
        Dictionary<string, dynamic> txJson,
        IEnumerable<XrplWallet> wallets,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        var walletList = wallets as IList<XrplWallet> ?? wallets.ToList();
        if (walletList.Count == 0)
        {
            throw new ValidationException("No wallets provided");
        } 
        var walletByAddr = walletList.ToDictionary(w => w.ClassicAddress, StringComparer.Ordinal);

        if (!txJson.TryGetValue("Account", out var mainAccObj))
        {
            throw new ValidationException("Main account not defined in tx json");
        }    
        var mainAcc = (string)mainAccObj;

        if (autofill)
        {
            txJson = await client.Autofill(txJson, signersCount: walletList.Count, cancellationToken: cancellationToken);
        }

        var root = JObject.FromObject(txJson);
        var rawArray = root["RawTransactions"] as JArray ?? new JArray();

        // 1) подписи владельцев внутренних tx
        var partialBlobs = new List<string>();
        foreach (var entry in rawArray.OfType<JObject>())
        {
            var acct = (string?)entry["RawTransaction"]?["Account"];
            if (string.IsNullOrWhiteSpace(acct))
            {
                throw new ValidationException("Inner tx missing Account");
            }
            if (mainAcc == acct)
            {
                // не ставим подписи на внутри батча для аккаунта создателя, только верхняя подпись
                continue;
            }

            // account_info со списком подписантов
            var ai = await client.AccountInfo(
                new AccountInfoRequest(acct)
                {
                    SignerLists = true
                }, cancellationToken);
            var hasSL = ai.SignerLists?.Length > 0 && ai.AccountFlags!.DisableMasterKey;
            if (hasSL)
            {
                var sl = ai.SignerLists[0];
                var (picked, sum) = BatchSigningHelper.PickWalletsForQuorum(sl, walletByAddr);

                if (sum < sl.SignerQuorum)
                {
                    throw new ValidationException($"Not enough signer wallets for multisig account {acct}.");
                }
                foreach (var wlt in picked)
                {
                    partialBlobs.Add(wlt.SignAsBatchPart(txJson, multisign: true, signingFor: acct).TxBlob);
                }
            }
            else
            {
                if (walletByAddr.TryGetValue(acct, out var owner) && !ai.AccountFlags!.DisableMasterKey)
                    partialBlobs.Add(owner.SignAsBatchPart(txJson, multisign: false, signingFor: acct).TxBlob);
                else if (!string.IsNullOrEmpty(ai.AccountData.RegularKey) &&
                         walletByAddr.TryGetValue(ai.AccountData.RegularKey, out var rk))
                    partialBlobs.Add(rk.SignAsBatchPart(txJson, multisign: false, signingFor: acct).TxBlob);
                else
                    throw new ValidationException($"Wallet for account {acct} (or its RegularKey) not provided");
            }
        }

        // 2) склейка внутренних подписей
        var combined = XrplWallet.CombineBatchSigners(partialBlobs.ToArray());
        var combinedJson = JObject.Parse(XrplBinaryCodec.Decode(combined.TxBlob).ToString());
        // 3) корневая подпись: single-sig ИЛИ multi-sig по наличию SignerList у корня
        var aiRoot = await client.AccountInfo(
            new AccountInfoRequest(mainAcc)
            {
                SignerLists = true
            }, cancellationToken);
        var rootHasSL = aiRoot.SignerLists?.Length > 0 && aiRoot.AccountFlags!.DisableMasterKey;
        if (!rootHasSL)
        {
            // обычная подпись плательщика комиссии (должен быть в wallets)
            if (!walletByAddr.TryGetValue(mainAcc, out var main))
                throw new ValidationException($"Main account {mainAcc} not found in provided wallets");
            var final = main.Sign(JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(combinedJson.ToString()));
            var submit = await client.SubmitRequest(final.TxBlob, failHard, cancellationToken);
            //var txRes = XrplBinaryCodec.Decode(submit.TxBlob);
            return submit;
        }
        else
        {
            // мультисиг корня: берём из wallets только тех, кто входит в SignerList(main)
            var sl = aiRoot.SignerLists[0];
            var (picked, sum) = BatchSigningHelper.PickWalletsForQuorum(sl, walletByAddr);

            if (sum < sl.SignerQuorum) throw new ValidationException($"Not enough signer wallets for root multisig {mainAcc}.");

            //// корневой мультисиг: обязательно пустой SPK и без TxnSignature
            //combinedJson.Remove("TxnSignature");
            //combinedJson["SigningPubKey"] = "";

            var msBlobs = picked.Select(w => w.Sign(
                JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(combinedJson.ToString()),
                multisign: true).TxBlob).ToArray();
            var msCombined = Signer.Multisign(msBlobs);
            //var txRes = XrplBinaryCodec.Decode(msCombined);

            var submit = await client.SubmitRequest(msCombined, failHard, cancellationToken);
            return submit;
        }
    }

    /// <summary>
    /// Encodes and submits a Batch signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="autofill">autofill transaction missed fields</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMultiBatch(
    this IXrplClient client,
    Batch tx,
    IEnumerable<XrplWallet> wallets,
    bool autofill = true,
    bool failHard = false,
    CancellationToken cancellationToken = default)
    {
        var json = tx.ToJson();
        var txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json)
                    ?? throw new ValidationException("Failed to deserialize tx json");

        var response = await client.SubmitMultiBatch(txJson, wallets, autofill, failHard, cancellationToken);
        return response;
    }

    /// <summary>
    /// The core logic of reliable submission.This polls the ledger until the result of the
    /// transaction can be considered final, meaning it has either been included in a
    /// validated ledger, or the transaction's lastLedgerSequence has been surpassed by the
    /// latest ledger sequence (meaning it will never be included in a validated ledger).
    /// </summary>
    /// <param name="client"></param>
    /// <param name="txHash"></param>
    /// <param name="lastLedgerSequence"></param>
    /// <param name="submissionResult"></param>
    /// <returns></returns>
    /// <exception cref="ValidationException"></exception>
    private static async Task<TransactionSummary> WaitForFinalTransactionOutcome(
        this IXrplClient client,
        string txHash,
        uint? lastLedgerSequence,
        string submissionResult,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Ждём закрытие следующего леджера
            await Task.Delay(LEDGER_CLOSE_TIME, cancellationToken);

            var latestLedger = await client.GetLedgerIndex(cancellationToken);

            // Если у нас есть LastLedgerSequence и мы его уже перешагнули — транзакция точно не попадёт в леджер
            if (lastLedgerSequence.HasValue && latestLedger > lastLedgerSequence.Value)
            {
                throw new ValidationException(
                    $"Transaction {txHash} has expired. " +
                    $"Latest ledger: {latestLedger}, LastLedgerSequence: {lastLedgerSequence}. " +
                    $"Preliminary result: {submissionResult}");
            }

            TransactionSummary txResponse;

            try
            {
                txResponse = await client.TxV2(
                    new TxRequest(txHash)
                    {
                        ApiVersion = 2,
                    }, cancellationToken);
            }
            catch (RippledException ex) when (ex.Response?.Error == XrplErrorCodes.TxnNotFound)
            {
                continue;
            }
            catch (RippledException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new XrplException(
                    $"Unexpected error while waiting for transaction {txHash}.\n" +
                    $"Preliminary result: {submissionResult}.\n" +
                    $"Details: {ex.Message}", ex);
            }

            // Как только транзакция валидирована — это финальный результат (успех или ошибка)
            if (txResponse.Validated == true)
            {
                return txResponse;
            }

            if (submissionResult != "tesSUCCESS" && submissionResult != "terQUEUED")
            { // Ошибочная транзакция тоже финальна, не валидируется сетью в большинстве случаев.
                throw new RippleException($"Final tx result is not success: {submissionResult}");
            }

            // Не валидирована и не txnNotFound → просто ждём дальше
        }
    }

    /// <summary>
    /// Initializes a transaction for a submit request
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign & encode, and submit.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>A Wallet derived from a seed.</returns>
    public static async Task<(string txBlob, Dictionary<string, dynamic> tx)> GetSignedTx(
        this IXrplClient client,
        Dictionary<string, dynamic> transaction,
        bool autofill = false,
        bool failHard = false,
        XrplWallet? wallet = null,
        CancellationToken cancellationToken = default
    )
    {
        //if (IsSigned(transaction))
        //{
        //    return transaction
        //}

        if (wallet == null)
        {
            throw new ValidationException("Wallet must be provided when submitting an unsigned transaction");
        }

        var tx = transaction;

        //var tx = transaction is string 
        //    ? // eslint-disable-next-line @typescript-eslint/consistent-type-assertions -- converts JsonObject to correct Transaction type
        //      (decode(transaction) as unknown as TransactionCommon)
        //    : transaction
        if (autofill)
        {
            tx = await client.Autofill(tx, cancellationToken: cancellationToken);
        }

        return (wallet.Sign(tx, multisign: false).TxBlob, tx);
    }

    public static bool IsSigned(object transaction)
    {
        if (transaction is Dictionary<string, dynamic> { } tx)
        {
            return (tx.TryGetValue(key: "SigningPubKey", value: out var SigningPubKey) && SigningPubKey is not null) ||
                   (tx.TryGetValue(key: "TxnSignature", value: out var TxnSignature) && TxnSignature is not null);
        }
        else
        {
            var ob = XrplBinaryCodec.Encode(transaction);
            var json = JObject.Parse($"{ob}");
            return (json.TryGetValue(propertyName: "SigningPubKey", value: out var SigningPubKey) &&
                    !string.IsNullOrWhiteSpace(SigningPubKey.ToString())) ||
                   (json.TryGetValue(propertyName: "TxnSignature", value: out var TxnSignature) &&
                    !string.IsNullOrWhiteSpace(TxnSignature.ToString()));
        }
    }

    /// <summary>
    /// checks if there is a LastLedgerSequence as a part of the transaction
    /// </summary>
    /// <param name="transaction">tx</param>
    /// <returns></returns>
    public static uint? GetLastLedgerSequence(object transaction) => LedgerSequenceHelper.GetLastLedgerSequence(transaction);

    /// <summary>
    /// checks if the transaction is an AccountDelete transaction
    /// </summary>
    /// <param name="transaction">tx</param>
    /// <returns></returns>
    public static bool IsAccountDelete(object transaction)
    {
        if (transaction is Dictionary<string, dynamic> { } tx)
        {
            return tx.TryGetValue(key: "TransactionType", value: out var TransactionType) &&
                   $"{TransactionType}" == "AccountDelete";
        }
        else if (transaction is TransactionRequest txc)
        {
            return txc.TransactionType == TransactionType.AccountDelete;
        }
        else
        {
            var ob = XrplBinaryCodec.Encode(transaction);
            var json = JObject.Parse($"{ob}");

            return json.TryGetValue(propertyName: "TransactionType", value: out var TransactionType) &&
                   TransactionType.ToString() == "AccountDelete";
        }
    }
}