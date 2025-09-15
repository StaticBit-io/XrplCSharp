using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Ledger;
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
        bool autofill = false,
        bool failHard = false,
        XrplWallet wallet = null
    )
    {
        var (signedTx, _) = await client.GetSignedTx(transaction, autofill, failHard: false, wallet);
        return await SubmitRequest(client, signedTx, failHard);
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
    public static async Task<TransactionSummary> SubmitAndWait(
        this IXrplClient client,
        Dictionary<string, dynamic> transaction,
        bool autofill = false,
        bool failHard = false,
        XrplWallet wallet = null)
    {
        var (signedTx, tx) = await client.GetSignedTx(transaction, autofill, failHard, wallet);
        var lastLedger = GetLastLedgerSequence(tx);
        if (lastLedger == null)
        {
            throw new ValidationException(
                "Transaction must contain a LastLedgerSequence value for reliable submission.");
        }

        var response = await client.SubmitRequest(signedTx, failHard);
        var txHash = HashLedger.HashSignedTx(signedTx);
        return await WaitForFinalTransactionOutcome(
            client,
            txHash,
            lastLedger,
            response.EngineResult);
    }

    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="signedTransaction">signed Transaction</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitRequest(this IXrplClient client, object signedTransaction, bool failHard)
    {
        //todo activate after fix
        //if (!IsSigned(signedTransaction))
        //{
        //    throw new ValidationException("Transaction must be signed");
        //}

        var signedTxEncoded = signedTransaction is string transaction
            ? transaction
            : XrplBinaryCodec.Encode(signedTransaction);
        var request = new SubmitRequest
        {
            Command = "submit",
            TxBlob = signedTxEncoded,
            FailHard = failHard,
        };
        var response = await client.GRequest<Submit, SubmitRequest>(request);
        return response;
    }

    /// <summary>
    /// Encodes and submits a signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMulti(
        this IXrplClient client,
        ITransactionCommon tx,
        IEnumerable<XrplWallet> wallets,
        bool autofill = true,
        bool failHard = false)
    {
        if (wallets is null)
        {
            throw new ValidationException("Wallets must be provided when submitting an unsigned transaction");
        }

        var json = tx.ToJson();

        //var json = JsonConvert.SerializeObject(tx);
        var txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);

        if (autofill)
        {
            txJson = await client.Autofill(txJson, signersCount: wallets.Count());
        }

        var signed = wallets.Select(c => c.Sign(txJson, multisign: true).TxBlob).ToArray();
        var combined = XrplWallet.CombineMultiSigners(signed);
        var response = await client.SubmitRequest(combined, failHard: false);
        return response;
    }

    /// <summary>
    /// Encodes and submits a Batch signed transaction.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="wallets">wallets for signer</param>
    /// <param name="failHard">If true, and the transaction fails locally, do not retry or relay the transaction to other servers.</param>
    /// <param name="tx">transaction for submit</param>
    /// <returns></returns>
    public static async Task<Submit> SubmitMultiBatch(
    this IXrplClient client,
    Batch tx,
    IEnumerable<XrplWallet> wallets,
    bool autofill = true,
    bool failHard = false)
    {
        if (wallets is null)
            throw new ValidationException("Wallets must be provided when submitting an unsigned transaction");

        var walletList = wallets as IList<XrplWallet> ?? wallets.ToList();
        if (walletList.Count == 0)
            throw new ValidationException("No wallets provided");

        // Быстрый доступ по адресу
        var walletByAddr = walletList.ToDictionary(w => w.ClassicAddress, StringComparer.Ordinal);

        if (!walletByAddr.TryGetValue(tx.Account, out var main))
            throw new ValidationException($"Main account {tx.Account} not found in provided wallets");

        // Сырые JSON-структуры
        var json = tx.ToJson();
        var txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json)
                    ?? throw new ValidationException("Failed to deserialize tx json");

        if (autofill)
        {
            // signersCount можно оценивать по числу участников; оставлю как было (wallets.Count()),
            // чтобы не менять внешний контракт.
            txJson = await client.Autofill(txJson, signersCount: walletList.Count);
        }

        // Все аккаунты-участники внутренних tx, кроме основного
        var participantAccounts = new HashSet<string>(
            tx.RawTransactions
                 .Select(rt => rt.RawTransaction?.Account)
                 .Where(a => !string.IsNullOrEmpty(a))
                 .Where(a => !string.Equals(a, tx.Account, StringComparison.Ordinal)),
            StringComparer.Ordinal);

        // Для каждого участника соберём подписи (параллельно)
        var tasks = participantAccounts.Select(async acc =>
        {
            var accountInfo = await client.AccountInfo(
                new AccountInfoRequest(acc)
                {
                    SignerLists = true
                });

            if (accountInfo.AccountFlags.DisableMasterKey == false)
            {
                if (walletByAddr.TryGetValue(acc, out var ownerWallet))
                {
                    var blob = ownerWallet
                        .SignAsBatchPart(txJson, multisign: false, acc)
                        .TxBlob;

                    return [blob,];
                }

                if (accountInfo.AccountData.RegularKey is not { Length: > 0 } &&
                    accountInfo.SignerLists is not { Length: > 0 })
                {
                    throw new ValidationException($"Wallet for account {acc} not provided");
                }
            }
            
            if (accountInfo.AccountData.RegularKey is { Length: > 0 } regularKey &&
                walletByAddr.TryGetValue(regularKey, out var regularWallet))
            {
                var blob = regularWallet
                    .SignAsBatchPart(txJson, multisign: false, acc)
                    .TxBlob;
                return [blob,];
            }

            var signerList = accountInfo.SignerLists
                .FirstOrDefault();

            if (signerList is null)
            {
                throw new ValidationException($"Wallet for account {acc} not provided");
            }

            // Есть мультисиг — выбираем минимально-достаточный набор по сумме весов
            var quorum = signerList.SignerQuorum;

            var candidates = signerList.SignerEntries
                .Select(se => new
                {
                    Addr = se.SignerEntry.Account,
                    Weight = se.SignerEntry.SignerWeight
                })
                .Where(se => walletByAddr.ContainsKey(se.Addr))
                .OrderByDescending(se => se.Weight) // жадно: самые тяжёлые сначала
                .ToList();

            if (candidates.Count == 0)
                throw new ValidationException($"No local signer keys for multisig account {acc}");

            uint sum = 0;
            var chosen = new List<string>();
            foreach (var cnd in candidates)
            {
                chosen.Add(cnd.Addr);
                sum += cnd.Weight;
                if (sum >= quorum) break;
            }

            if (sum < quorum)
            {
                var have = string.Join(", ", candidates.Select(e => $"{e.Addr}:{e.Weight}"));
                throw new ValidationException(
                    $"Not enough signer weight for {acc}. Quorum={quorum}, available={sum}. Signers: {have}");
            }

            // Подписываем выбранными кошельками
            var blobs = new List<string>(chosen.Count);
            foreach (var addr in chosen)
            {
                var w = walletByAddr[addr];
                var b = w.SignAsBatchPart(txJson, multisign: true, acc).TxBlob;
                blobs.Add(b);
            }

            return blobs.ToArray();
        });

        var signedGroups = await Task.WhenAll(tasks);
        var signedTxs = signedGroups.SelectMany(x => x).ToArray();

        // Склеиваем подписи и отправляем
        var combined = XrplWallet.CombineBatchSigners(signedTxs);
        var txRes = XrplBinaryCodec.Decode(combined.TxBlob);

        txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(
                     JsonConvert.SerializeObject(txRes))
                 ?? throw new ValidationException("Failed to prepare signed tx json");

        var signed = main.Sign(txJson);
        txRes = XrplBinaryCodec.Decode(signed.TxBlob);

        var response = await client.SubmitRequest(signed.TxBlob, failHard);

        return response;
    }

    //public static async Task<Submit> SubmitMultiBatch(
    //    this IXrplClient client,
    //    ITransactionCommon tx,
    //    IEnumerable<XrplWallet> wallets,
    //    bool autofill = true,
    //    bool failHard = false)
    //{
    //    if (wallets is null)
    //    {
    //        throw new ValidationException("Wallets must be provided when submitting an unsigned transaction");
    //    }

    //    if (tx is not Batch batch)
    //    {
    //        throw new ValidationException("Tx must be Batch transaction");
    //    }

    //    var json = tx.ToJson();

    //    //var json = JsonConvert.SerializeObject(tx);
    //    var txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);

    //    if (autofill)
    //    {
    //        txJson = await client.Autofill(txJson, signersCount: wallets.Count());
    //    }

    //    var main = wallets.First(c => c.ClassicAddress == tx.Account);
    //    var walletsTxs = batch.RawTransactions.Select(c => c.RawTransaction).Select(c => c.Account).Distinct();

    //    var signedTxs = wallets.Where(c => walletsTxs.Contains(c.ClassicAddress) && c.ClassicAddress != tx.Account)
    //        .SelectMany(c =>
    //        {
    //            var accSigner = await client.AccountObjects(
    //                new AccountObjectsRequest(c.ClassicAddress)
    //                {
    //                    Type = LedgerEntryType.SignerList,
    //                });
    //            if (accSigner.AccountObjectList is { Count: > 0, } accountObjects)
    //            {
    //                var signers = new List<SignatureResult>();
    //                foreach (var accObject in accountObjects)
    //                {
    //                    if (accObject is LOSignerList signer)
    //                    {
    //                        var quorum = signer.SignerQuorum;

    //                        var signerAccounts = signer.SignerEntries.Select(a => a.SignerEntry.Account);
    //                        var selectedWallets =
    //                            wallets.Where(w => signerAccounts.Contains(w.ClassicAddress)).ToList();
    //                        if (selectedWallets.Count < quorum)
    //                        {
    //                            throw new ValidationException(
    //                                $"Not enough signers in wallet {c.ClassicAddress} for quorum {quorum}");
    //                        }

    //                        return selectedWallets.Select(w =>
    //                            w.SignAsBatchPart(txJson, multisign: false, c.ClassicAddress).TxBlob);
    //                    }
    //                }
    //            }

    //            return [c.SignAsBatchPart(txJson, multisign: false, c.ClassicAddress).TxBlob,];
    //        }).ToArray();

    //    // 3) Склеиваем подписи и отправляем
    //    var combined = XrplWallet.CombineBatchSigners(signedTxs);
    //    var txRes = XrplBinaryCodec.Decode(combined.TxBlob);

    //    txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(JsonConvert.SerializeObject(txRes));
    //    var signed = main.Sign(txJson);
    //    var response = await client.SubmitRequest(signed.TxBlob, failHard: false);
    //    return response;
    //}

    /// <summary>
    /// The core logic of reliable submission.This polls the ledger until the result of the
    /// transaction can be considered final, meaning it has either been included in a
    /// validated ledger, or the transaction's lastLedgerSequence has been surpassed by the
    /// latest ledger sequence (meaning it will never be included in a validated ledger).
    /// </summary>
    /// <param name="Client"></param>
    /// <param name="TxHash"></param>
    /// <param name="lastLedger"></param>
    /// <param name="submissionResult"></param>
    /// <returns></returns>
    /// <exception cref="ValidationException"></exception>
    private static async Task<TransactionSummary> WaitForFinalTransactionOutcome(
        this IXrplClient Client,
        string TxHash,
        uint? lastLedger,
        string submissionResult)
    {
        await Task.Delay(LEDGER_CLOSE_TIME);
        var latestLedger = await Client.GetLedgerIndex();
        if (lastLedger < latestLedger)
        {
            throw new ValidationException(
                "The latest ledger sequence ${ latestLedger } is greater than the transaction's LastLedgerSequence (${lastLedger}).\n" +
                $"Preliminary result: {submissionResult}");
        }

        TransactionSummary txResponse = null;
        try
        {
            txResponse = await Client.TxV2(
                new TxRequest(TxHash)
                {
                    ApiVersion = 2,
                });
        }
        catch (Exception error)
        {
            // error is of an unknown type and hence we assert type to extract the value we need.
            var message = error?.Data["Error"] as string;
            if (message == "txnNotFound")
            {
                return await WaitForFinalTransactionOutcome(Client, TxHash, lastLedger, submissionResult);
            }

            throw new ValidationException(
                $"{message} \n Preliminary result: {submissionResult}.\nFull error details: {error.Message}");
        }

        if (txResponse.Validated == true)
        {
            return txResponse;
        }

        return await WaitForFinalTransactionOutcome(Client, TxHash, lastLedger, submissionResult);
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
        XrplWallet? wallet = null
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
            tx = await client.Autofill(tx);
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
    public static uint? GetLastLedgerSequence(object transaction)
    {
        if (transaction is Dictionary<string, dynamic> { } tx)
        {
            return tx.TryGetValue(key: "LastLedgerSequence", value: out var LastLedgerSequence) &&
                   LastLedgerSequence is uint
                ? LastLedgerSequence
                : null;
        }
        else if (transaction is TransactionCommon txc)
        {
            return txc.LastLedgerSequence;
        }

        else
        {
            var ob = XrplBinaryCodec.Encode(transaction);
            var json = JObject.Parse($"{ob}");

            return json.TryGetValue(propertyName: "LastLedgerSequence", value: out var LastLedgerSequence) &&
                   uint.TryParse(s: LastLedgerSequence.ToString(), result: out var seq)
                ? seq
                : null;
        }
    }

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
        else if (transaction is TransactionCommon txc)
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