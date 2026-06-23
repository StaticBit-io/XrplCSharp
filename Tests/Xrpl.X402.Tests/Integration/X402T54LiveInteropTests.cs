using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;
using Xrpl.X402;
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Integration;

/// <summary>
/// Live interop test that pins our x402 wire format against the real t54 facilitator
/// (<c>https://xrpl-facilitator-testnet.t54.ai</c>) on public testnet.
/// <para>
/// This test is intentionally <see cref="IgnoreAttribute">ignored</see> in CI.
/// Run manually to verify end-to-end interoperability with the live t54 service.
/// </para>
/// <para>
/// G5 fix (2026-06-23): aligned to t54 reference payer (x402-xrpl 0.2.0):
/// <list type="bullet">
///   <item><c>payload.invoiceId</c> = raw invoice id string (now included in PAYMENT-SIGNATURE).</item>
///   <item><c>Payment.InvoiceID</c> = SHA-256(UTF-8(invoiceId)) uppercase hex.</item>
///   <item>Memo.MemoData = UTF-8 hex of raw invoiceId (only MemoData, no MemoType/MemoFormat).</item>
///   <item>Default binding = <c>Both</c> (matches t54 reference default <c>invoice_binding = "both"</c>).</item>
///   <item>SourceTag only from <c>extra.sourceTag</c>; no hardcoded default.</item>
///   <item>invoiceId is now any non-empty string (no 64-hex requirement).</item>
/// </list>
/// </para>
/// <para>
/// G6 PASS (2026-06-23): adding <c>extra["sourceTag"] = 804681468</c> resolves <c>source_tag_mismatch</c>.
/// t54 /verify returns <c>{"isValid":true,"invalidReason":null}</c>.
/// t54 settle tx hash: <c>8DB5B4144A24E7D72FED584D8B0EAFFE19B9034FE5EC3DD296B19FED5731B7E8</c>.
/// Full live t54 interop confirmed on testnet.
/// </para>
/// </summary>
[TestClass]
public class X402T54LiveInteropTests
{
    private const string TestnetUrl = "wss://s.altnet.rippletest.net:51233";
    private const string T54BaseUrl = "https://xrpl-facilitator-testnet.t54.ai";

    // Plain-string invoiceId — any non-empty string; t54 binding reads invoiceId from extra
    private const string LiveInvoiceId = "inv-live-001";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Full end-to-end x402 flow against the real t54 facilitator on testnet.
    /// Uses default <c>X402IntentBinding.Both</c>: sets both <c>Payment.InvoiceID</c> (SHA-256)
    /// and a Memo (UTF-8 hex). <c>payload.invoiceId</c> = raw invoice id string.
    /// </summary>
    // Live test: hits public testnet faucet + the t54 hosted facilitator (no SLA).
    // Kept out of the repo's TestI/TestU CI filters by name so a t54/testnet hiccup
    // never reds an unrelated CI run; runs in a plain `dotnet test`.
    // G6 PASS (2026-06-23): sourceTag=804681468 in extra resolves source_tag_mismatch;
    // /verify returns {"isValid":true,"invalidReason":null}; settle tx 8DB5B414...B7E8.
    [TestMethod]
    public async Task TestT54LiveSettlesXrpOnTestnet()
    {
        // ── 1. Connect to public testnet ───────────────────────────────────────────
        XrplClient client = new(TestnetUrl);
        await client.Connect();

        try
        {
            // ── 2. Fund payer and merchant via testnet faucet ──────────────────────
            XrplWallet payer = XrplWallet.Generate();
            XrplWallet merchant = XrplWallet.Generate();

            WalletSugar.Funded payerFunded = await client.FundWallet(payer);
            WalletSugar.Funded merchantFunded = await client.FundWallet(merchant);

            Console.WriteLine($"[T54-LIVE] payer    = {payer.ClassicAddress} ({payerFunded.Balance} XRP)");
            Console.WriteLine($"[T54-LIVE] merchant = {merchant.ClassicAddress} ({merchantFunded.Balance} XRP)");

            Console.WriteLine($"[T54-LIVE] invoiceId = {LiveInvoiceId}");

            // ── 3. Build requirement & payment ────────────────────────────────────
            // invoiceId is a plain string; Payment.InvoiceID = SHA-256(UTF-8(invoiceId))
            PaymentRequirement requirement = new()
            {
                Scheme = "exact",
                Network = "xrpl:1",
                Asset = "XRP",
                PayTo = merchant.ClassicAddress,
                Amount = "1000000",
                MaxTimeoutSeconds = 600,
                Extra = new()
                {
                    ["invoiceId"] = JsonDocument.Parse($"\"{LiveInvoiceId}\"").RootElement,
                    ["sourceTag"] = JsonDocument.Parse("804681468").RootElement
                }
            };

            // Default IntentBinding = Both: sets Payment.InvoiceID = SHA-256(inv) + Memo.MemoData = UTF8-hex(inv)
            IX402Signer signer = new XrplWalletX402Signer(client, payer);
            (Xrpl.Models.Transactions.Payment payment, string resolvedInv) =
                X402PaymentBuilder.BuildWithInvoiceId(requirement, payer.ClassicAddress);

            Console.WriteLine($"[T54-LIVE] payment.InvoiceID = {payment.InvoiceID}");
            Console.WriteLine($"[T54-LIVE] payment.SourceTag = {payment.SourceTag}");
            Console.WriteLine($"[T54-LIVE] memo.MemoData     = {payment.Memos?[0].Memo.MemoData}");
            Console.WriteLine($"[T54-LIVE] resolvedInv       = {resolvedInv}");

            string signedBlob = await signer.PrepareAndSignAsync(payment, requirement.MaxTimeoutSeconds);
            Console.WriteLine($"[T54-LIVE] signedBlob (first 80) = {signedBlob[..Math.Min(80, signedBlob.Length)]}...");

            // payload.invoiceId = raw invoice id (G5 fix)
            PaymentSignatureEnvelope envelope = new()
            {
                X402Version = 2,
                Accepted = requirement,
                Payload = new SignedPayload { SignedTxBlob = signedBlob, InvoiceId = resolvedInv }
            };

            // Build request body
            object requestBody = new
            {
                paymentPayload = envelope,
                paymentRequirements = requirement
            };

            // ── 4. Call /verify directly and print verbatim response ──────────────
            using HttpClient httpClient = new();

            using HttpResponseMessage verifyResp = await httpClient.PostAsJsonAsync(
                $"{T54BaseUrl}/verify", requestBody, _jsonOpts);

            string verifyBody = await verifyResp.Content.ReadAsStringAsync();
            Console.WriteLine($"[T54-LIVE] /verify status = {(int)verifyResp.StatusCode}");
            Console.WriteLine($"[T54-LIVE] /verify body   = {verifyBody}");

            // ── 5. Call T54Facilitator (verify → settle) ──────────────────────────
            T54Facilitator t54 = new(new HttpClient());
            PaymentResponseEnvelope receipt = await t54.VerifyAndSettleAsync(envelope);

            Console.WriteLine($"[T54-LIVE] receipt.Success      = {receipt.Success}");
            Console.WriteLine($"[T54-LIVE] receipt.Transaction   = {receipt.Transaction}");
            Console.WriteLine($"[T54-LIVE] receipt.ErrorReason   = {receipt.ErrorReason}");

            Assert.IsTrue(receipt.Success,
                $"t54 settlement failed: errorReason={receipt.ErrorReason}");
            Assert.IsFalse(string.IsNullOrEmpty(receipt.Transaction),
                "Expected a non-empty transaction hash from t54");
        }
        finally
        {
            await client.Disconnect();
        }
    }

    // Live t54 RLUSD/IOU interop. Uses our OWN testnet issuer with the RLUSD currency code:
    // real RLUSD has no programmatic testnet faucet, and t54 settles any valid IOU Payment that
    // matches the requirement (it does not validate the issuer is Ripple's real RLUSD).
    // Exercises the IOU wire path: Amount object + SendMax + InvoiceID(SHA256) + memo + sourceTag.
    [TestMethod]
    public async Task TestT54LiveSettlesRlusdOnTestnet()
    {
        const string Rlusd = "524C555344000000000000000000000000000000";
        const string RlusdInvoiceId = "inv-live-rlusd-001";

        XrplClient client = new(TestnetUrl);
        await client.Connect();

        try
        {
            XrplWallet issuer = XrplWallet.Generate();
            XrplWallet payer = XrplWallet.Generate();
            XrplWallet merchant = XrplWallet.Generate();

            await client.FundWallet(issuer);
            await client.FundWallet(payer);
            await client.FundWallet(merchant);
            Console.WriteLine($"[T54-LIVE-RLUSD] issuer={issuer.ClassicAddress} payer={payer.ClassicAddress} merchant={merchant.ClassicAddress}");

            // Issuer enables rippling (else payer→merchant rippling through the issuer is tecPATH_DRY).
            await SubmitOrThrow(client, new AccountSet
            {
                Account = issuer.ClassicAddress,
                SetFlag = AccountSetAsfFlags.asfDefaultRipple
            }.ToDictionary(), issuer, "AccountSet(DefaultRipple)");

            // Trustlines payer→issuer and merchant→issuer for RLUSD.
            await SubmitOrThrow(client, new TrustSet
            {
                Account = payer.ClassicAddress,
                LimitAmount = new Currency { CurrencyCode = Rlusd, Issuer = issuer.ClassicAddress, Value = "1000000" }
            }.ToDictionary(), payer, "TrustSet(payer)");

            await SubmitOrThrow(client, new TrustSet
            {
                Account = merchant.ClassicAddress,
                LimitAmount = new Currency { CurrencyCode = Rlusd, Issuer = issuer.ClassicAddress, Value = "1000000" }
            }.ToDictionary(), merchant, "TrustSet(merchant)");

            // Issuer issues 100 RLUSD to payer.
            await SubmitOrThrow(client, new Payment
            {
                Account = issuer.ClassicAddress,
                Destination = payer.ClassicAddress,
                Amount = new Currency { CurrencyCode = Rlusd, Issuer = issuer.ClassicAddress, Value = "100" }
            }.ToDictionary(), issuer, "Issue RLUSD");

            PaymentRequirement requirement = new()
            {
                Scheme = "exact",
                Network = "xrpl:1",
                Asset = Rlusd,
                PayTo = merchant.ClassicAddress,
                Amount = "2.5",
                MaxTimeoutSeconds = 600,
                Extra = new()
                {
                    ["invoiceId"] = JsonDocument.Parse($"\"{RlusdInvoiceId}\"").RootElement,
                    ["issuer"] = JsonDocument.Parse($"\"{issuer.ClassicAddress}\"").RootElement,
                    ["sourceTag"] = JsonDocument.Parse("804681468").RootElement
                }
            };

            IX402Signer signer = new XrplWalletX402Signer(client, payer);
            (Payment payment, string resolvedInv) = X402PaymentBuilder.BuildWithInvoiceId(requirement, payer.ClassicAddress);

            Console.WriteLine($"[T54-LIVE-RLUSD] InvoiceID={payment.InvoiceID} SourceTag={payment.SourceTag}");
            Console.WriteLine($"[T54-LIVE-RLUSD] Amount={JsonSerializer.Serialize(payment.Amount)} SendMax={JsonSerializer.Serialize(payment.SendMax)}");

            string signedBlob = await signer.PrepareAndSignAsync(payment, requirement.MaxTimeoutSeconds);

            PaymentSignatureEnvelope envelope = new()
            {
                X402Version = 2,
                Accepted = requirement,
                Payload = new SignedPayload { SignedTxBlob = signedBlob, InvoiceId = resolvedInv }
            };

            using HttpClient httpClient = new();
            using HttpResponseMessage verifyResp = await httpClient.PostAsJsonAsync(
                $"{T54BaseUrl}/verify",
                new { paymentPayload = envelope, paymentRequirements = requirement },
                _jsonOpts);
            Console.WriteLine($"[T54-LIVE-RLUSD] /verify status={(int)verifyResp.StatusCode} body={await verifyResp.Content.ReadAsStringAsync()}");

            T54Facilitator t54 = new(new HttpClient());
            PaymentResponseEnvelope receipt = await t54.VerifyAndSettleAsync(envelope);
            Console.WriteLine($"[T54-LIVE-RLUSD] receipt.Success={receipt.Success} tx={receipt.Transaction} err={receipt.ErrorReason}");

            Assert.IsTrue(receipt.Success, $"t54 RLUSD settlement failed: errorReason={receipt.ErrorReason}");
            Assert.IsFalse(string.IsNullOrEmpty(receipt.Transaction), "Expected a non-empty tx hash from t54");
        }
        finally
        {
            await client.Disconnect();
        }
    }

    private static async Task SubmitOrThrow(IXrplClient client, Dictionary<string, object> tx, XrplWallet wallet, string label)
    {
        var summary = await client.SubmitAndWait(tx, wallet: wallet);
        string result = summary.Meta?.TransactionResult ?? "(none)";
        if (!summary.Validated || !result.StartsWith("tes", StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} did not validate: validated={summary.Validated} result={result}");
        Console.WriteLine($"[T54-LIVE-RLUSD] {label} -> {result} ({summary.Hash})");
    }
}
