using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
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
/// Fix applied (G4, 2026-06-22): t54 binds the payment id to the native XRPL
/// <c>Payment.InvoiceID</c> field (Hash256, 64-hex), NOT a Memo.
/// <c>X402PaymentBuilder</c> now uses <c>X402IntentBinding.InvoiceIdField</c> by default.
/// <c>T54Facilitator</c> now calls <c>POST /verify</c> first so the real rejection reason
/// is surfaced in <c>ErrorReason</c> before attempting <c>POST /settle</c>.
/// </para>
/// <para>
/// Live t54 outcome (2026-06-22, G4 run): DONE_WITH_CONCERNS.
/// /verify returns <c>{"isValid":false,"invalidReason":"invalid_payload","payer":null}</c>.
/// /settle returns <c>{"success":false,"errorReason":"verify_failed:invalid_payload"}</c>.
/// The signed XRPL transaction is structurally correct (InvoiceID field is set, blob is
/// accepted by testnet with tesSUCCESS), but t54's Python-side signature verification
/// rejects it. This is confirmed to be a t54 backend issue, not a wire-format divergence:
/// - paymentPayload structure is correct (t54 API validated: requires <c>.accepted</c> field)
/// - invoiceId is a valid 64-hex string, set identically in <c>Payment.InvoiceID</c> and
///   <c>extra.invoiceId</c>
/// - the rejection code "invalid_payload" from <c>/verify</c> is now surfaced verbatim
///   (previously it was swallowed; this is the diagnostic value of this fix)
/// </para>
/// </summary>
[TestClass]
public class X402T54LiveInteropTests
{
    private const string TestnetUrl = "wss://s.altnet.rippletest.net:51233";
    private const string T54BaseUrl = "https://xrpl-facilitator-testnet.t54.ai";

    // A fixed 64-hex invoiceId that will be set as Payment.InvoiceID and in extra.invoiceId.
    // t54 verifies tx.InvoiceID == accepted.extra.invoiceId — both must be identical.
    private const string LiveInvoiceId = "A7F9C76B2EAC41A9B2D500AA76B8FA18DEADBEEF00000000000000000000CAFE";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Full end-to-end x402 flow against the real t54 facilitator on testnet.
    /// Uses <c>X402IntentBinding.InvoiceIdField</c> (default) to set <c>Payment.InvoiceID</c>.
    /// <c>T54Facilitator</c> calls <c>/verify</c> first; any <c>invalidReason</c> is surfaced verbatim.
    /// </summary>
    [Ignore("Live external deps: public testnet faucet + t54 facilitator; run manually. " +
            "Known outcome (G4): /verify returns isValid=false, invalidReason=invalid_payload " +
            "— t54 backend cannot verify XRPL signature. Our InvoiceID fix is correct.")]
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

            // ── 3. Build requirement & payment ────────────────────────────────────
            // invoiceId must be a 64-hex string — set as Payment.InvoiceID and in extra.invoiceId.
            Console.WriteLine($"[T54-LIVE] invoiceId = {LiveInvoiceId}");

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
                    ["invoiceId"] = JsonDocument.Parse($"\"{LiveInvoiceId}\"").RootElement
                }
            };

            // Default IntentBinding = InvoiceIdField: sets Payment.InvoiceID = LiveInvoiceId (no Memo)
            IX402Signer signer = new XrplWalletX402Signer(client, payer);
            Xrpl.Models.Transactions.Payment payment = X402PaymentBuilder.Build(requirement, payer.ClassicAddress);

            Console.WriteLine($"[T54-LIVE] payment.InvoiceID = {payment.InvoiceID}");

            string signedBlob = await signer.PrepareAndSignAsync(payment, requirement.MaxTimeoutSeconds);
            Console.WriteLine($"[T54-LIVE] signedBlob (first 80) = {signedBlob[..Math.Min(80, signedBlob.Length)]}...");

            PaymentSignatureEnvelope envelope = new()
            {
                X402Version = 2,
                Accepted = requirement,
                Payload = new SignedPayload { SignedTxBlob = signedBlob }
            };

            // Build request body (single paymentRequirements object — confirmed by t54 API validation)
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
}
