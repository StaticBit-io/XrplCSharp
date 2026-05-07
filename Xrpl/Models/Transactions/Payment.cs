using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;
using Xrpl.Models.Methods;
using Xrpl.Models.Utils;

using Index = Xrpl.Models.Utils.Index;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/payment.ts

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Enum representing values for Payment Transaction Flags.
    /// </summary>
    [Flags]
    public enum PaymentFlags : uint
    {
        /// <summary>
        /// batch inner transaction
        /// </summary>
        tfInnerBatchTxn = XrplGlobalFlags.tfInnerBatchTxn,

        /// <summary>
        /// Do not use the default path;<br/>
        /// only use paths included in the Paths field.<br/>
        /// This is intended to force the transaction to take arbitrage opportunities.<br/>
        /// Most clients do not need this.
        /// </summary>
        tfNoDirectRipple = 65536,
        /// <summary>
        /// If the specified Amount cannot be sent without spending more than SendMax, reduce the received amount instead of failing outright.<br/>
        /// See Partial Payments for more details.
        /// </summary>
        tfPartialPayment = 131072,
        /// <summary>
        /// Only take paths where all the conversions have an input:output ratio that is equal or better than the ratio of Amount:SendMax.<br/>
        /// See Limit Quality for details.
        /// </summary>
        tfLimitQuality = 262144,
    }

    /// <inheritdoc cref="IPayment" />
    public class Payment : TransactionRequest, IPayment, IDestination
    {
        public Payment()
        {
            TransactionType = TransactionType.Payment;
        }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DeliverMax")]
        [JsonConverter(typeof(CurrencyConverter))]
        private Currency? DeliverMax
        {
            set => Amount = value;
        }

        /// <inheritdoc />
        public string Destination { get; set; }

        /// <inheritdoc />
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        public new PaymentFlags? Flags
        {
            get => base.Flags.HasValue ? (PaymentFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        } 
        /// <inheritdoc />
        public string InvoiceID { get; set; }

        /// <inheritdoc />
        public List<List<Path>> Paths { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SendMax { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DeliverMin { get; set; }

        /// <inheritdoc />
        public string DomainID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CredentialIDs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> CredentialIDs { get; set; }
    }

    /// <summary>
    /// A Payment transaction represents a transfer of value from one account to  another.
    /// </summary>
    /// <code>
    /// ```typescript
    /// const partialPayment: Payment =
    /// {
    ///         TransactionType: 'Payment',
    ///         Account: 'rM9WCfJU6udpFkvKThRaFHDMsp7L8rpgN',
    ///         Amount:{
    ///         currency: 'FOO',
    ///         value: '4000',
    ///         issuer: 'rPzwM2JfCSDjhbesdTCqFjWWdK7eFtTwZz',
    ///     },
    ///         Destination: 'rPzwM2JfCSDjhbesdTCqFjWWdK7eFtTwZz',
    ///         Flags:{
    ///         tfPartialPayment: true
    ///     }
    /// }
    /// ```
    /// </code>
    public interface IPayment : ITransactionCommon, IDestination
    {
        /// <summary>
        /// API v1
        /// The amount of currency to deliver.<br/>
        /// For non-XRP amounts, the nested field names MUST be lower-case.<br/>
        /// If the tfPartialPayment flag is set, deliver up to this amount instead.
        /// Alias to DeliverMax <br/>
        /// API v2: The maximum amount of currency to deliver.<br/>
        /// Partial payments can deliver less than this amount and still succeed;<br/>
        /// other payments fail unless they deliver the exact amount.
        /// </summary>
        Currency? Amount { get; set; }

        /// <summary>
        /// Minimum amount of destination currency this transaction should deliver.<br/>
        /// Only valid if this is a partial payment.<br/>
        /// For non-XRP amounts, the nested field names are lower-case.
        /// </summary>
        Currency? DeliverMin { get; set; }
        /// <summary>
        /// The unique address of the account receiving the payment.
        /// </summary>
        string Destination { get; set; }
        /// <summary>
        /// Arbitrary tag that identifies the reason for the payment to the destination, or a hosted recipient to pay.
        /// </summary>
        uint? DestinationTag { get; set; }
        /// <summary>
        /// Payment Transaction Flags
        /// </summary>
        new PaymentFlags? Flags { get; set; }
        /// <summary>
        /// Arbitrary 256-bit hash representing a specific reason or identifier for this payment.
        /// </summary>
        string InvoiceID { get; set; }
        /// <summary>
        /// Array of payment paths to be used for this transaction.<br/>
        /// Must be omitted for XRP-to-XRP transactions.
        /// </summary>
        List<List<Path>> Paths { get; set; }
        /// <summary>
        /// Highest amount of source currency this transaction is allowed to cost, including transfer fees, exchange rates, and slippage.<br/>
        /// Does not include the XRP destroyed as a cost for submitting the transaction.<br/>
        /// For non-XRP amounts, the nested field names MUST be lower-case.<br/>
        /// Must be supplied for cross-currency/cross-issue payments.<br/>
        /// Must be omitted for XRP-to-XRP Payments.
        /// </summary>
        Currency SendMax { get; set; }
        /// <summary>
        /// The domain the sender intends to use for cross-currency payments through the permissioned DEX.
        /// Both sender and destination must be part of this domain if it interacts with the DEX.
        /// </summary>
        string DomainID { get; set; }

        /// <summary>
        /// (Optional) Set of Credentials (object IDs, hex 64-char each) to authorize a deposit
        /// when the destination account requires Deposit Authorization with credential-based preauth (XLS-70).
        /// Maximum 8 entries.
        /// </summary>
        List<string> CredentialIDs { get; set; }
    }

    /// <inheritdoc cref="IPayment" />
    public class PaymentResponse : TransactionResponse, IPayment, IDestination
    {
        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DeliverMax")]
        [JsonConverter(typeof(CurrencyConverter))]
        private Currency? DeliverMax
        {
            set => Amount = value;
        }

        /// <inheritdoc />
        public string Destination { get; set; }

        /// <inheritdoc />
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        public new PaymentFlags? Flags
        {
            get => base.Flags.HasValue ? (PaymentFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        public string InvoiceID { get; set; }

        /// <inheritdoc />
        public List<List<Path>> Paths { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SendMax { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DeliverMin { get; set; }

        /// <inheritdoc />
        public string DomainID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CredentialIDs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> CredentialIDs { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a Payment at runtime.
        /// </summary>
        /// <param name="tx"> A Payment Transaction.</param>
        /// <exception cref="ValidationException">When the Payment is malformed.</exception>
        public static async Task ValidatePayment(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Amount", out var Amount) || Amount is null)
                throw new ValidationException("PaymentTransaction: missing field Amount");

            if (!Common.IsAmount(Amount))
                throw new ValidationException("PaymentTransaction: invalid Amount");


            if (!tx.TryGetValue("Destination", out var Destination) || Destination is null)
                throw new ValidationException("PaymentTransaction: missing field Destination");
            if (!Common.IsAmount(Destination))
                throw new ValidationException("PaymentTransaction: invalid Destination");

            if (tx.TryGetValue("DestinationTag", out var DestinationTag) && DestinationTag is not uint { })
                throw new ValidationException("PaymentTransaction: DestinationTag must be a number");

            if (tx.TryGetValue("InvoiceID", out var InvoiceID) && InvoiceID is not string { })
                throw new ValidationException("PaymentTransaction: InvoiceID must be a string");
            if (tx.TryGetValue("Paths", out var Paths) && !IsPaths(Paths as List<List<Dictionary<string, object>>>))
                throw new ValidationException("PaymentTransaction: invalid Paths");
            if (tx.TryGetValue("SendMax", out var SendMax) && !Common.IsAmount(SendMax))
                throw new ValidationException("PaymentTransaction: invalid SendMax");

            if (tx.TryGetValue("DomainID", out var domainId) && domainId is not null)
            {
                if (domainId is not string domainIdStr)
                    throw new ValidationException("PaymentTransaction: DomainID must be a string");
                if (domainIdStr.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(domainIdStr, "^[0-9A-Fa-f]{64}$"))
                    throw new ValidationException("PaymentTransaction: DomainID must be a 64-character hexadecimal string (256-bit hash)");
            }

            if (tx.TryGetValue("CredentialIDs", out var credentialIds) && credentialIds is not null)
            {
                CredentialsValidator.ValidateCredentialsList(credentialIds, "PaymentTransaction", "CredentialIDs", isStringID: true);
            }

            await CheckPartialPayment(tx);
        }

        public static Task CheckPartialPayment(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("DeliverMin", out var DeliverMin)) 
                return Task.CompletedTask;

            if (tx.TryGetValue("Flags", out var flags))
            {
                if (flags is null)
                    throw new ValidationException("PaymentTransaction: tfPartialPayment flag required with DeliverMin");
            }

            bool isTfPartialPayment = flags is uint uFlag
                ? Index.IsFlagEnabled(uFlag, (uint)PaymentFlags.tfPartialPayment)
                : flags is PaymentFlags pf 
                    ? pf == PaymentFlags.tfPartialPayment 
                    : flags is Dictionary<string, object> flagDict && CheckFlag<PaymentFlags>(flagDict, "tfPartialPayment");
            if (!isTfPartialPayment)
                throw new ValidationException("PaymentTransaction: tfPartialPayment flag required with DeliverMin");
            if (!Common.IsAmount(DeliverMin))
                throw new ValidationException("PaymentTransaction: invalid DeliverMin");

            return Task.CompletedTask;
        }
        static bool CheckFlag<T>(Dictionary<string, object> flag, string type) where T : Enum
        {
            if (flag.TryGetValue(type, out object f) && f is true)
            {
                return true;
            }

            return false;

        }
        public static bool IsPathStep(Dictionary<string, object> pathStep)
        {
            if (pathStep.TryGetValue("account", out var acc) && acc is not string { })
                return false;
            if (pathStep.TryGetValue("currency", out var currency) && currency is not string { })
                return false;
            if (pathStep.TryGetValue("issuer", out var issuer) && issuer is not string { })
                return false;

            if (acc is not null && currency is null && issuer is null)
                return true;
            if (currency is not null || issuer is not null)
                return true;
            return false;
        }
        public static bool IsPaths(List<Dictionary<string, object>> paths)
        {
            foreach (var path in paths)
            {
                if (!IsPathStep(path))
                    return false;
            }

            return true;

        }
        public static bool IsPaths(List<List<Dictionary<string, object>>> paths)
        {
            if (paths is null || paths.Count == 0)
                return false;
            foreach (var c in paths)
            {
                if (c is null || c.Count == 0) return false;
                if (!IsPaths(c)) return false;
            }
            return true;
        }
    }

}
