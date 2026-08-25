using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using JsonSerializer = System.Text.Json.JsonSerializer;

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
        Dictionary<string, object> transaction,
        bool autofill = true,
        bool failHard = false,
        XrplWallet wallet = null,
        CancellationToken cancellationToken = default
    )
    {
        var (signedTx, _) = await client.GetSignedTx(transaction, autofill, wallet, cancellationToken);
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
        Dictionary<string, object> transaction,
        XrplWallet wallet = null,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        var (signedTx, tx) = await client.GetSignedTx(transaction, autofill, wallet, cancellationToken);
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
        var tx = JsonNode.Parse(decoded)?.AsObject();
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
        var signedTxEncoded = GetTxBlob(signedTransaction);

        var request = new SubmitRequest
        {
            Command = "submit",
            TxBlob = signedTxEncoded,
            FailHard = failHard,
        };
        var response = await client.GRequest<Submit, SubmitRequest>(request, cancellationToken).Typed();
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
        var txJson = JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default)
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
        Dictionary<string, object> tx,
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
        Dictionary<string, object> txJson,
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

        var root = JsonNode.Parse(JsonSerializer.Serialize(txJson, XrplJsonOptions.Default))?.AsObject();
        var rawArray = root["RawTransactions"]?.AsArray() ?? new JsonArray();

        // 1) подписи владельцев внутренних tx
        var partialBlobs = new List<string>();
        foreach (var entry in rawArray.Where(n => n is JsonObject).Select(n => n!.AsObject()))
        {
            var acct = entry["RawTransaction"]?["Account"]?.GetValue<string>();
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
                }, cancellationToken).Typed();
            // Both are read below to decide how this account signs, and a response missing either
            // is malformed rather than a signer with no flags: the master-key check would otherwise
            // throw NullReferenceException here, and the RegularKey lookup further down would do
            // the same. Failing with the account named beats either.
            if (ai.AccountData is null)
            {
                throw new ValidationException($"account_info response for '{acct}' did not include account_data.");
            }

            if (ai.AccountFlags is null)
            {
                throw new ValidationException($"account_info response for '{acct}' did not include the account's flags.");
            }

            var hasSL = ai.SignerLists?.Length > 0 && ai.AccountFlags.DisableMasterKey;
            if (hasSL)
            {
                var sl = ai.SignerLists[0];
                var (picked, sum, quorum) = BatchSigningHelper.PickWalletsForQuorum(sl, walletByAddr);

                if (sum < quorum)
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
                if (walletByAddr.TryGetValue(acct, out var owner) && !ai.AccountFlags.DisableMasterKey)
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
        var combinedJson = JsonNode.Parse(XrplBinaryCodec.Decode(combined.TxBlob).ToJsonString())?.AsObject();
        // 3) корневая подпись: single-sig ИЛИ multi-sig по наличию SignerList у корня
        var aiRoot = await client.AccountInfo(
            new AccountInfoRequest(mainAcc)
            {
                SignerLists = true
            }, cancellationToken).Typed();
        // Same shape as the per-account check above: the master-key flag decides how the root
        // signs, and a response without flags is malformed rather than an account with none.
        if (aiRoot.AccountFlags is null)
        {
            throw new ValidationException($"account_info response for '{mainAcc}' did not include the account's flags.");
        }

        var rootHasSL = aiRoot.SignerLists?.Length > 0 && aiRoot.AccountFlags.DisableMasterKey;
        if (!rootHasSL)
        {
            // обычная подпись плательщика комиссии (должен быть в wallets)
            if (!walletByAddr.TryGetValue(mainAcc, out var main))
                throw new ValidationException($"Main account {mainAcc} not found in provided wallets");
            var final = main.Sign(JsonSerializer.Deserialize<Dictionary<string, object>>(combinedJson.ToJsonString(), XrplJsonOptions.Default));
            var submit = await client.SubmitRequest(final.TxBlob, failHard, cancellationToken);
            //var txRes = XrplBinaryCodec.Decode(submit.TxBlob);
            return submit;
        }
        else
        {
            // мультисиг корня: берём из wallets только тех, кто входит в SignerList(main)
            var sl = aiRoot.SignerLists[0];
            var (picked, sum, quorum) = BatchSigningHelper.PickWalletsForQuorum(sl, walletByAddr);

            if (sum < quorum) throw new ValidationException($"Not enough signer wallets for root multisig {mainAcc}.");

            //// корневой мультисиг: обязательно пустой SPK и без TxnSignature
            //combinedJson.Remove("TxnSignature");
            //combinedJson["SigningPubKey"] = "";

            var msBlobs = picked.Select(w => w.Sign(
                JsonSerializer.Deserialize<Dictionary<string, object>>(combinedJson.ToJsonString(), XrplJsonOptions.Default),
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
        var txJson = JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default)
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

            TransactionSummary txResponse;

            try
            {
                txResponse = await client.TxV2(
                    new TxRequest(txHash)
                    {
                        ApiVersion = 2,
                    }, cancellationToken).Typed();
            }
            catch (RippledException ex) when (ex.Response?.Error == XrplErrorCodes.TxnNotFound)
            {
            	// Если у нас есть LastLedgerSequence и мы его уже перешагнули — транзакция точно не попадёт в леджер
                var latestLedger = await client.GetLedgerIndex(cancellationToken);
                if (lastLedgerSequence.HasValue && latestLedger > lastLedgerSequence.Value)
                {
                    throw new ValidationException(
                        $"Transaction {txHash} has expired. " +
                        $"Latest ledger: {latestLedger}, LastLedgerSequence: {lastLedgerSequence}. " +
                        $"Preliminary result: {submissionResult}");
                }
                continue;
            }
            catch (RippledException)
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

            if (txResponse.Validated == true)
            {
                string txResult = txResponse.Meta?.TransactionResult;
                if (txResult != null && !txResult.StartsWith("tes") && txResult != "terQUEUED")
                {
                    // Applied to a ledger: the fee was taken and there is a transaction to look up,
                    // so the summary travels with the failure. The message is unchanged - what a
                    // caller needs in order to act sits beside it, not inside it.
                    throw new TransactionFailedException(
                        $"Final tx result is not success: {txResult}",
                        engineResult: txResult,
                        hash: txHash,
                        result: txResponse);
                }
                return txResponse;
            }

            if (submissionResult != "tesSUCCESS" && submissionResult != "terQUEUED")
            {
                // Reached when the transaction is not validated yet and the node's provisional
                // answer was already a failure. Final enough to stop waiting on - but not all the
                // same kind of failure: a tem or a tef never reaches a ledger and costs nothing,
                // while a tec was applied and the fee is gone, it simply has not been validated at
                // the moment this is noticed. Hence no summary here for either, and hence
                // ReachedLedger reading the code rather than the absence of one.
                throw new TransactionFailedException(
                    $"Final tx result is not success: {submissionResult}",
                    engineResult: submissionResult,
                    hash: txHash);
            }

            // Не валидирована и не txnNotFound → просто ждём дальше после проверки текущего леджера
            var currentLedger = await client.GetLedgerIndex(cancellationToken);
            if (lastLedgerSequence.HasValue && currentLedger > lastLedgerSequence.Value)
            {
                throw new ValidationException(
                    $"Transaction {txHash} has expired. " +
                    $"Latest ledger: {currentLedger}, LastLedgerSequence: {lastLedgerSequence}. " +
                    $"Preliminary result: {submissionResult}");
            }
        }
    }

    /// <summary>
    /// Initializes a transaction for a submit request
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction to autofill, sign and encode.</param>
    /// <param name="autofill">If true, autofill a transaction.</param>
    /// <param name="wallet">A wallet to sign a transaction. It must be provided when submitting an unsigned transaction.</param>
    /// <returns>The signed transaction blob and the transaction it was built from.</returns>
    public static async Task<(string txBlob, Dictionary<string, object> tx)> GetSignedTx(
        this IXrplClient client,
        Dictionary<string, object> transaction,
        bool autofill = false,
        XrplWallet? wallet = null,
        CancellationToken cancellationToken = default,
        bool sponsorPreCheck = true
    )
    {
        if (wallet == null)
        {
            throw new ValidationException("Wallet must be provided when submitting an unsigned transaction");
        }

        var tx = transaction;

        bool isSponsored = tx.TryGetValue("Sponsor", out var sponsorField) && sponsorField is string;
        string? sponsorAddress = isSponsored ? (string)sponsorField : null;
        // The main signature is either a single TxnSignature or multisig Signers
        bool hasMainSignature =
            (tx.TryGetValue("TxnSignature", out var mainSig) && mainSig is string { Length: > 0 }) ||
            (tx.TryGetValue("Signers", out var mainSigners) && mainSigners is not null);
        // ANY signature material freezes the body: a co-signature was computed
        // over these exact fields, so autofill would silently invalidate it
        bool hasAnySignature = hasMainSignature ||
            (tx.TryGetValue("SponsorSignature", out var sponsorSigMaterial) && sponsorSigMaterial is not null) ||
            (tx.TryGetValue("CounterpartySignature", out var counterpartySigMaterial) && counterpartySigMaterial is not null);

        if (autofill && !hasAnySignature)
        {
            tx = await client.Autofill(tx, cancellationToken: cancellationToken);
        }

        if (isSponsored && string.Equals(sponsorAddress, wallet.ClassicAddress, StringComparison.Ordinal))
        {
            // The sponsor finalizes: the sponsee's signature must already be present —
            // the sponsor cannot produce it
            if (!hasMainSignature)
            {
                throw new ValidationException("Sponsored transaction is not signed by all participants: the submitter's TxnSignature is missing and the sponsor cannot produce it.");
            }

            string mainBlob = XrplBinaryCodec.Encode(tx);
            SignatureResult sponsorPart = wallet.Sign(tx, multisign: false); // routes to the sponsor path
            SignatureResult final = SignatureComposer.ComposeSignatures(new[] { mainBlob, sponsorPart.TxBlob });
            return (final.TxBlob, tx);
        }

        if (isSponsored && sponsorPreCheck)
        {
            bool hasSponsorSignature = tx.TryGetValue("SponsorSignature", out var sponsorSigValue) && sponsorSigValue is not null;
            if (!hasSponsorSignature &&
                await IsSponsorSignatureRequired(client, tx, sponsorAddress!, cancellationToken))
            {
                throw new ValidationException("Sponsored transaction is not signed by all participants: the sponsorship requires the sponsor's co-signature (SponsorSignature) for this coverage.");
            }
        }

        return (wallet.Sign(tx, multisign: false).TxBlob, tx);
    }

    /// <summary>
    /// Submits a sponsored transaction with both keys available locally (the
    /// V1 flow in one call): autofills, prepares, co-signs with the sponsee
    /// and the sponsor, submits and waits for the final outcome.
    /// </summary>
    /// <param name="client">A Client.</param>
    /// <param name="transaction">A transaction carrying Sponsor/SponsorFlags.</param>
    /// <param name="sponseeWallet">The submitting account's wallet.</param>
    /// <param name="sponsorWallet">The sponsor's wallet (must match tx.Sponsor).</param>
    /// <param name="autofill">If true, autofill the transaction.</param>
    /// <param name="failHard">If true, do not retry or relay on local failure.</param>
    public static async Task<TransactionSummary> SubmitAndWaitSponsored(
        this IXrplClient client,
        Dictionary<string, object> transaction,
        XrplWallet sponseeWallet,
        XrplWallet sponsorWallet,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default)
    {
        if (sponseeWallet is null || sponsorWallet is null)
        {
            throw new ValidationException("Both the sponsee and the sponsor wallets must be provided.");
        }

        var tx = transaction;
        if (autofill)
        {
            tx = await client.Autofill(tx, cancellationToken: cancellationToken);
        }

        JsonObject prepared = JsonNode.Parse(JsonSerializer.Serialize(tx, XrplJsonOptions.Default))?.AsObject()
            ?? throw new ValidationException("Failed to serialize transaction to JSON");
        prepared["SigningPubKey"] = sponseeWallet.PublicKey;
        prepared.Remove("SponsorSignature");
        prepared.Remove("TxnSignature");

        var signed = SponsorSigningHelper.SignSponsored(prepared, sponseeWallet, sponsorWallet);
        return await client.SubmitRequestAndWait(signed.TxBlob, failHard, cancellationToken);
    }

    /// <summary>
    /// Submits a sponsored transaction with both keys available locally (the
    /// V1 flow in one call).
    /// </summary>
    public static Task<TransactionSummary> SubmitAndWaitSponsored(
        this IXrplClient client,
        ITransactionRequest transaction,
        XrplWallet sponseeWallet,
        XrplWallet sponsorWallet,
        bool autofill = true,
        bool failHard = false,
        CancellationToken cancellationToken = default) =>
        SubmitAndWaitSponsored(client, transaction.ToDictionary(), sponseeWallet, sponsorWallet, autofill, failHard, cancellationToken);

    /// <summary>
    /// Checks the Sponsorship ledger object's require-sign flags against the
    /// transaction's SponsorFlags coverage. Returns false when the relationship
    /// does not exist (the node will reject the transaction with a clear code).
    /// </summary>
    internal static async Task<bool> IsSponsorSignatureRequired(
        IXrplClient client,
        Dictionary<string, object> tx,
        string sponsorAddress,
        CancellationToken cancellationToken = default)
    {
        if (!tx.TryGetValue("Account", out var accountField) || accountField is not string account)
            return false;
        uint coverage = tx.TryGetValue("SponsorFlags", out var flagsField) &&
                        Models.Transactions.Common.TryGetUInt32(flagsField, out uint parsed)
            ? parsed
            : 0;
        if (coverage == 0)
            return false;

        var request = new Models.Methods.AccountObjectsRequest(sponsorAddress)
        {
            Type = Models.LedgerEntryType.Sponsorship,
        };
        var response = await client.AccountObjects(request, cancellationToken).Typed().ConfigureAwait(false);
        var sponsorship = response?.AccountObjectList?
            .OfType<Models.Ledger.LOSponsorship>()
            .FirstOrDefault(s => string.Equals(s.Sponsee, account, StringComparison.Ordinal));
        if (sponsorship is null)
            return false;

        // A missing Flags value is equivalent to "no flags set" for a bitmask check, so false is the correct
        // (not a fabricated) default here - unlike numeric fields (Sequence, balances) where 0 would be a lie.
        bool requireForFee = sponsorship.Flags?.HasFlag(Models.Ledger.SponsorshipFlags.lsfSponsorshipRequireSignForFee) ?? false;
        bool requireForReserve = sponsorship.Flags?.HasFlag(Models.Ledger.SponsorshipFlags.lsfSponsorshipRequireSignForReserve) ?? false;

        return ((coverage & (uint)SponsorCoverage.spfSponsorFee) != 0 && requireForFee)
            || ((coverage & (uint)SponsorCoverage.spfSponsorReserve) != 0 && requireForReserve);
    }

    public static bool IsSigned(object transaction)
    {
        if (transaction is Dictionary<string, object> { } tx)
        {
            return (tx.TryGetValue(key: "SigningPubKey", value: out var SigningPubKey) && SigningPubKey is not null) ||
                   (tx.TryGetValue(key: "TxnSignature", value: out var TxnSignature) && TxnSignature is not null);
        }
        else
        {
            var ob = XrplBinaryCodec.Encode(transaction);
            var json = JsonNode.Parse($"{ob}")?.AsObject();
            if (json == null) return false;
            return (json.TryGetPropertyValue("SigningPubKey", out var SigningPubKey) &&
                    !string.IsNullOrWhiteSpace(SigningPubKey?.ToString())) ||
                   (json.TryGetPropertyValue("TxnSignature", out var TxnSignature) &&
                    !string.IsNullOrWhiteSpace(TxnSignature?.ToString()));
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
        if (transaction is Dictionary<string, object> { } tx)
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
            var json = JsonNode.Parse($"{ob}")?.AsObject();
            if (json == null) return false;

            return json.TryGetPropertyValue("TransactionType", out var TransactionType) &&
                   TransactionType?.ToString() == "AccountDelete";
        }
    }
}