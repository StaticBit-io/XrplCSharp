using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.AddressCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Utils;

using static Xrpl.AddressCodec.XrplAddressCodec;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/sugar/autofill.ts

namespace Xrpl.Sugar
{
    public class AddressNTag
    {
        public string ClassicAddress { get; set; }
        public uint? Tag { get; set; }
    }

    public static class AutofillSugar
    {
        const int LEDGER_OFFSET = 20;
        
        /// <summary>
        /// Devnet has minimum fee 0.0000001 instead of 0.000001 (7 digits after dot).
        /// This multiplier corrects the base fee for devnet transactions.
        /// </summary>
        const int DEVNET_FEE_CORRECTION_MULTIPLIER = 12;
        
        /// <summary>
        /// Batch transactions have a base fee multiplier of 3.
        /// </summary>
        const int BATCH_BASE_FEE_MULTIPLIER = 3;

        /// <summary>
        /// rippled kConfidentialFeeMultiplier: extra base fee units charged to confidential MPT transactions.
        /// </summary>
        const int CONFIDENTIAL_FEE_MULTIPLIER = 9;

        /// <summary>
        /// rippled lending::kLoanPaymentsPerFeeIncrement: loan payments covered by one base fee increment.
        /// </summary>
        const int LOAN_PAYMENTS_PER_FEE_INCREMENT = 5;

        /// <summary>
        /// rippled lending::kLoanMaximumPaymentsPerTransaction: payments a single LoanPay ever processes.
        /// </summary>
        const int LOAN_MAX_PAYMENTS_PER_TRANSACTION = 100;

        /// <summary>
        /// Upper bound on LoanPay fee increments, mirroring rippled kMaxFeeIncrements.
        /// </summary>
        const int LOAN_MAX_FEE_INCREMENTS = LOAN_MAX_PAYMENTS_PER_TRANSACTION / LOAN_PAYMENTS_PER_FEE_INCREMENT;


        /// <summary>
        /// Autofills fields in a transaction. This will set `Sequence`, `Fee`,
        /// `lastLedgerSequence` according to the current state of the server this Client
        /// is connected to. It also converts all X-Addresses to classic addresses and
        /// flags interfaces into numbers.
        /// </summary>
        /// <param name="client">A client.</param>
        /// <param name="transaction">A {@link Transaction} in JSON format</param>
        /// <param name="signersCount">The expected number of signers for this transaction. Only used for multisigned transactions.</param>
        /// <returns>The autofilled transaction.</returns>
        public static async Task<Dictionary<string, object>> Autofill(this IXrplClient client, Dictionary<string, object> transaction, int? signersCount, CancellationToken cancellationToken = default)
        {

            Dictionary<string, object> tx = transaction;

            tx.SetValidAddresses();

            //Flags.SetTransactionFlagsToNumber(tx);
            List<Task> promises = new List<Task>();
            bool hasTT = tx.TryGetValue("TransactionType", out var tt);
            string txType = $"{tt}";
            if (!tx.ContainsKey("Sequence") && txType != "Batch")
            {
                promises.Add(client.SetNextValidSequenceNumber(tx, cancellationToken));
            }
            if (!tx.ContainsKey("Fee"))
            {
                promises.Add(client.CalculateFeePerTransactionType(tx, signersCount ?? 0, cancellationToken));
            }
            if (!tx.ContainsKey("LastLedgerSequence"))
            {
                promises.Add(client.SetLatestValidatedLedgerSequence(tx, cancellationToken));
            }
            else if(tx.TryGetValue("LastLedgerSequence", out var lastLedgerValue) && lastLedgerValue is 0u or 0UL or 0L or 0)
            {
                tx.Remove("LastLedgerSequence");
            }
            if (txType == "Batch")
            {
                promises.Add(client.NormalizeBatchTransaction(tx, cancellationToken));
            }
            await Task.WhenAll(promises);
            //string jsonData = JsonConvert.SerializeObject(tx);
            return tx;
        }


        public static void SetValidAddresses(this Dictionary<string, object> tx)
        {
            tx.ValidateAccountAddress("Account", "SourceTag");
            if (tx.ContainsKey("Destination"))
            {
                tx.ValidateAccountAddress("Destination", "DestinationTag");
            }

            // DepositPreauth:
            tx.ConvertToClassicAddress("Authorize");
            tx.ConvertToClassicAddress("Unauthorize");
            // EscrowCancel, EscrowFinish:
            tx.ConvertToClassicAddress("Owner");
            // SetRegularKey:
            tx.ConvertToClassicAddress("RegularKey");
        }

        public static void ValidateAccountAddress(this Dictionary<string, object> tx, string accountField, string tagField)
        {
            // if X-address is given, convert it to classic address
            var ainfo = tx.TryGetValue(accountField, out var aField);

            AddressNTag classicAccount = GetClassicAccountAndTag((string)aField, null);
            tx[accountField] = classicAccount.ClassicAddress;

            var tinfo = tx.TryGetValue(tagField, out var tField);

            // XRPL: Does bool or int. Smells.
            if (classicAccount.Tag != null)
            {
                if (tField != null && (int)tField != classicAccount.Tag)
                {
                    throw new ValidationException($"The {tagField}, if present, must match the tag of the {accountField} X - address");
                }
                // eslint-disable-next-line no-param-reassign -- param reassign is safe
                tx[tagField] = classicAccount.Tag;
            }
        }

        public static AddressNTag GetClassicAccountAndTag(this string account, uint? expectedTag)
        {
            if (!account.StartsWith('r') && XrplAddressCodec.IsValidXAddress(account))
            {
                CodecAddress codecAddress = XrplAddressCodec.XAddressToClassicAddress(account);
                if (expectedTag != null && codecAddress.Tag != expectedTag)
                {
                    throw new ValidationException("address includes a tag that does not match the tag specified in the transaction");
                }
                return new AddressNTag { ClassicAddress = codecAddress.ClassicAddress, Tag = codecAddress.Tag };
            }
            return new AddressNTag { ClassicAddress = account, Tag = expectedTag };
        }

        public static void ConvertToClassicAddress(this Dictionary<string, object> tx, string fieldName)
        {
            if (tx.ContainsKey(fieldName))
            {
                string account = (string)tx[fieldName];
                if (account is string)
                {
                    AddressNTag addressntag = account.GetClassicAccountAndTag(null);
                    tx[fieldName] = addressntag.ClassicAddress;
                }
            }
        }

        public static async Task<uint> SetNextValidSequenceNumber(this IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Current);
            AccountInfoRequest request = new AccountInfoRequest((string)tx["Account"]) { LedgerIndex = index };
            AccountInfo data = await client.AccountInfo(request, cancellationToken).Typed();
            // account_info returns the full AccountRoot for a live "current" ledger request, so
            // AccountData (and its Sequence) is always present; a missing AccountData or Sequence
            // means the node response is malformed and should fail loudly rather than dereference
            // a null AccountData or silently autofill 0.
            if (data.AccountData is null)
            {
                throw new XrplException("account_info response did not include account_data.");
            }

            uint? sequence = data.AccountData.Sequence;
            if (sequence == null)
            {
                throw new XrplException("account_info response did not include the account's Sequence.");
            }
            tx.TryAdd("Sequence", sequence.Value);
            return sequence.Value;
        }

        public static async Task<BigInteger> FetchReserveFee(this IXrplClient client, CancellationToken cancellationToken = default)
        {
            ServerStateRequest request = new ServerStateRequest();
            ServerState data = await client.ServerState(request, cancellationToken).Typed();

            // Checked before dereferencing, not after: reading through State.ValidatedLedger and
            // testing only the leaf turns a response missing either container into a
            // NullReferenceException, which says nothing about what the node returned.
            if (data.State?.ValidatedLedger is null)
            {
                throw new XrplException("server_state response did not include the validated ledger.");
            }

            uint? fee = data.State.ValidatedLedger.ReserveInc;

            if (fee == null)
            {
                throw new XrplException("Could not fetch Owner Reserve.");
            }
            return BigInteger.Parse(fee.Value.ToString());
        }

        public static async Task CalculateFeePerTransactionType(this IXrplClient client, Dictionary<string, object> tx, int signersCount = 0, CancellationToken cancellationToken = default)
        {
            var netFeeXRP = await client.GetFeeXrp(cancellationToken: cancellationToken);
            var netFeeDrops = XrpConversion.XrpToDrops(netFeeXRP);
            var baseFee = new BigInteger(Math.Floor(decimal.Parse(netFeeDrops, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)));
            
            // Devnet returns fees ~12x lower than mainnet. Detect by checking if baseFee < 10 drops.
            if (baseFee < 10)
            {
                baseFee *= DEVNET_FEE_CORRECTION_MULTIPLIER;
            }

            var transactionType = (string)tx["TransactionType"];
            var calculatedFee = await CalculateBaseFeeForType(client, tx, transactionType, baseFee, netFeeDrops, cancellationToken);
            // rippled Transactor::calculateBaseFee: one baseFee per outer multisig signer
            // plus one per signer nested in SponsorSignature.Signers (XLS-68 sponsor
            // multisig; a single-signed SponsorSignature adds nothing). Scaled from the
            // devnet-corrected baseFee so all charges use the same unit.
            var signerFee = baseFee * Math.Max(0, signersCount);
            var sponsorSignerFee = baseFee * GetSponsorSignerCount(tx);

            calculatedFee += signerFee + sponsorSignerFee;

            // rippled LoanPay::calculateBaseFee multiplies the whole Transactor cost —
            // signatures included — by one increment per kLoanPaymentsPerFeeIncrement payments.
            if (transactionType == nameof(TransactionType.LoanPay))
            {
                calculatedFee *= await GetLoanPayFeeIncrements(client, tx, cancellationToken);
            }

            BigInteger totalFee;
            if (!string.IsNullOrWhiteSpace(client.maxFeeXRP))
            {
                var maxFeeDrops = XrpConversion.XrpToDrops(client.maxFeeXRP);
                var maxFeeBI = new BigInteger(Math.Floor(decimal.Parse(maxFeeDrops, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)));
                totalFee = IsReserveFeeTxNeed(tx)
                    ? calculatedFee
                    : BigInteger.Min(calculatedFee, maxFeeBI);
            }
            else
            {
                totalFee = calculatedFee;
            }
            tx.TryAdd("Fee", Math.Ceiling((decimal)totalFee).ToString());
        }

        private static async Task<BigInteger> CalculateBaseFeeForType(
            IXrplClient client, 
            Dictionary<string, object> tx, 
            string transactionType, 
            BigInteger baseFee, 
            string netFeeDrops,
            CancellationToken cancellationToken = default)
        {
            return transactionType switch
            {
                "EscrowFinish" when tx.TryGetValue("Fulfillment", out _) => CalculateEscrowFinishFee(tx, netFeeDrops),
                "Batch" => await CalculateBatchFee(client, tx, baseFee, cancellationToken),
                nameof(TransactionType.LoanSet) => await CalculateLoanSetFee(client, tx, baseFee, cancellationToken),
                _ when IsConfidentialMPTTx(transactionType) => baseFee * (1 + CONFIDENTIAL_FEE_MULTIPLIER),
                _ when IsReserveFeeTxNeed(tx) => await FetchReserveFee(client, cancellationToken),
                _ => baseFee
            };
        }

        /// <summary>
        /// Confidential MPT transactions pay a flat extra multiplier for the cryptographic proofs
        /// they carry (rippled Transactor::calculateBaseFee with kConfidentialFeeMultiplier).
        /// </summary>
        private static bool IsConfidentialMPTTx(string transactionType)
        {
            return transactionType
                is nameof(TransactionType.ConfidentialMPTSend)
                or nameof(TransactionType.ConfidentialMPTConvert)
                or nameof(TransactionType.ConfidentialMPTConvertBack)
                or nameof(TransactionType.ConfidentialMPTMergeInbox)
                or nameof(TransactionType.ConfidentialMPTClawback);
        }

        /// <summary>
        /// Calculates fee for LoanSet, which charges one extra base fee per counterparty signature
        /// (rippled LoanSet::calculateBaseFee counts CounterpartySignature.Signers, or the single
        /// signature when present).
        /// </summary>
        /// <remarks>
        /// When the counterparty has not signed yet — the usual case during autofill — the signature
        /// count is unknown, so the counterparty's signer list size is used to avoid underpaying.
        /// </remarks>
        private static async Task<BigInteger> CalculateLoanSetFee(IXrplClient client, Dictionary<string, object> tx, BigInteger baseFee, CancellationToken cancellationToken = default)
        {
            int counterpartySigners = GetCounterpartySignerCount(tx);
            if (counterpartySigners == 0)
            {
                counterpartySigners = await FetchCounterpartySignerCount(client, tx, cancellationToken);
            }

            return baseFee * (1 + counterpartySigners);
        }

        /// <summary>
        /// Counts signatures already present in CounterpartySignature: every entry of a nested
        /// Signers array, or one for a single signature. Returns 0 when the field is absent.
        /// </summary>
        private static int GetCounterpartySignerCount(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("CounterpartySignature", out var counterpartySignature) || counterpartySignature == null)
                return 0;

            int nestedSigners = CountSigners(GetNestedField(counterpartySignature, "Signers"));
            if (nestedSigners > 0)
                return nestedSigners;

            return GetNestedField(counterpartySignature, "TxnSignature") != null ? 1 : 0;
        }

        /// <summary>
        /// Fetches the size of the counterparty's signer list, mirroring xrpl.js autofill:
        /// the counterparty may multi-sign, so the fee has to cover every possible signer.
        /// Falls back to a single signature when there is no signer list.
        /// </summary>
        private static async Task<int> FetchCounterpartySignerCount(IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            if (!tx.TryGetValue("Counterparty", out var counterparty) || counterparty is not string account || string.IsNullOrWhiteSpace(account))
                return 1;

            // Current, not validated: a signer list set in the last ledger has not been validated
            // yet, and missing it would underpay the fee.
            AccountInfoRequest request = new AccountInfoRequest(account)
            {
                LedgerIndex = new LedgerIndex(LedgerIndexType.Current),
                SignerLists = true,
            };

            try
            {
                AccountInfo data = await client.AccountInfo(request, cancellationToken).Typed();
                int? entries = data?.SignerLists?.Length > 0 ? data.SignerLists[0].SignerEntries?.Count : null;
                return entries is > 0 ? entries.Value : 1;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The counterparty account may not exist yet; preclaim rejects the transaction anyway.
                // The filter keeps a caller's cancellation out of that fallback: without it an
                // OperationCanceledException would be swallowed and autofill would carry on with a
                // guessed signer count instead of stopping. A timeout inside the client still falls
                // back, since it does not cancel this token.
                return 1;
            }
        }

        /// <summary>
        /// Number of base fee increments a LoanPay transaction is charged: one per
        /// kLoanPaymentsPerFeeIncrement payments the transaction is expected to process,
        /// capped at kLoanMaximumPaymentsPerTransaction / kLoanPaymentsPerFeeIncrement.
        /// Returns 1 whenever rippled falls back to the normal cost.
        /// </summary>
        private static async Task<BigInteger> GetLoanPayFeeIncrements(IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            uint flags = ParseTransactionFlags(tx);
            bool isFullPayment = (flags & (uint)LoanPayFlags.tfLoanFullPayment) != 0;
            bool isLatePayment = (flags & (uint)LoanPayFlags.tfLoanLatePayment) != 0;

            // A full or late payment performs one set of calculations regardless of the amount.
            if (isFullPayment || isLatePayment)
                return BigInteger.One;

            if (!tx.TryGetValue("LoanID", out var loanId) || loanId is not string id || string.IsNullOrWhiteSpace(id))
                return BigInteger.One;

            if (!TryGetLoanPayAmount(tx, out decimal amount, out bool integralAsset) || amount <= 0)
                return BigInteger.One;

            LOLoan loan = await FetchLoan(client, id, cancellationToken);
            if (loan == null)
                return BigInteger.One;

            // Fewer payments left than one increment covers: no extra work to charge for.
            if (loan.PaymentRemaining is null or <= LOAN_PAYMENTS_PER_FEE_INCREMENT)
                return BigInteger.One;

            if (!TryParseNumber(loan.PeriodicPayment, out decimal periodicPayment) || periodicPayment <= 0)
                return BigInteger.One;

            TryParseNumber(loan.LoanServiceFee, out decimal serviceFee);
            decimal regularPayment = RoundPeriodicPayment(periodicPayment, integralAsset, loan.LoanScale ?? 0) + serviceFee;
            if (regularPayment <= 0)
                return BigInteger.One;

            // The payment handler never processes more than kLoanMaximumPaymentsPerTransaction payments.
            // Divided rather than multiplied: a periodic payment near decimal.MaxValue overflows when
            // scaled up, while dividing the amount by a constant never can.
            if (amount / LOAN_MAX_PAYMENTS_PER_TRANSACTION >= regularPayment)
                return LOAN_MAX_FEE_INCREMENTS;

            // Overpayments do about as much work as a full payment, so they round up.
            bool isOverpayment = (flags & (uint)LoanPayFlags.tfLoanOverpayment) != 0;
            decimal paymentEstimate = amount / regularPayment;
            decimal payments = isOverpayment ? Math.Ceiling(paymentEstimate) : Math.Floor(paymentEstimate);

            decimal increments = Math.Ceiling(payments / LOAN_PAYMENTS_PER_FEE_INCREMENT);
            if (increments < 1)
                return BigInteger.One;

            return increments > LOAN_MAX_FEE_INCREMENTS ? LOAN_MAX_FEE_INCREMENTS : new BigInteger(increments);
        }

        /// <summary>
        /// Reads the Loan ledger object referenced by LoanPay. Returns null when it cannot be
        /// retrieved — rippled behaves the same way and lets preclaim report the error.
        /// </summary>
        private static async Task<LOLoan> FetchLoan(IXrplClient client, string loanId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Current, not validated: a loan created in the last ledger has not been validated
                // yet, and treating it as missing would underpay the fee.
                LedgerEntryRequest request = new LedgerEntryRequest
                {
                    Index = loanId,
                    LedgerIndex = new LedgerIndex(LedgerIndexType.Current),
                };
                LedgerEntryResponse response = await client.LedgerEntry(request, cancellationToken).Typed();
                return response?.Node as LOLoan;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Same reasoning as FetchCounterpartySignerCount: a missing object is a fallback,
                // a cancellation asked for by the caller is not.
                return null;
            }
        }

        /// <summary>
        /// rippled roundPeriodicPayment: integral assets (XRP, MPT) round up to whole units,
        /// IOUs round up to a multiple of 10^scale.
        /// </summary>
        private static decimal RoundPeriodicPayment(decimal periodicPayment, bool integralAsset, int scale)
        {
            if (integralAsset)
                return Math.Ceiling(periodicPayment);

            // Outside this range the step cannot be represented as a decimal; leave the value alone.
            if (scale is < -28 or > 28)
                return periodicPayment;

            decimal step = scale >= 0
                ? Pow10(scale)
                : 1m / Pow10(-scale);

            return Math.Ceiling(periodicPayment / step) * step;
        }

        private static decimal Pow10(int exponent)
        {
            decimal result = 1m;
            for (int i = 0; i < exponent; i++)
            {
                result *= 10m;
            }
            return result;
        }

        /// <summary>
        /// Extracts the LoanPay amount and whether its asset is integral (XRP drops or MPT units,
        /// which rippled rounds to whole units) as opposed to an IOU.
        /// </summary>
        private static bool TryGetLoanPayAmount(Dictionary<string, object> tx, out decimal amount, out bool integralAsset)
        {
            amount = 0m;
            integralAsset = true;

            if (!tx.TryGetValue("Amount", out var rawAmount) || rawAmount == null)
                return false;

            if (rawAmount is string xrpDrops)
                return TryParseNumber(xrpDrops, out amount);

            object value = GetNestedField(rawAmount, "value");
            if (value == null)
                return false;

            // MPT amounts are integral like XRP; issued currencies carry decimals.
            integralAsset = GetNestedField(rawAmount, "mpt_issuance_id") != null;
            return TryParseNumber(ToPlainValue(value), out amount);
        }

        private static uint ParseTransactionFlags(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("Flags", out var flags) || flags == null)
                return 0;

            return flags switch
            {
                uint u => u,
                int i when i >= 0 => (uint)i,
                long l when l is >= 0 and <= uint.MaxValue => (uint)l,
                LoanPayFlags f => (uint)f,
                JsonValue jv when jv.TryGetValue(out uint u) => u,
                JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetUInt32(out uint u) => u,
                string s when uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u) => u,
                _ => 0
            };
        }

        /// <summary>
        /// Parses a rippled Number field, which is serialized as a decimal string.
        /// </summary>
        private static bool TryParseNumber(object value, out decimal result)
        {
            result = 0m;
            return value != null
                && decimal.TryParse(
                    value as string ?? value.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out result);
        }

        private static object GetNestedField(object container, string fieldName)
        {
            return container switch
            {
                Dictionary<string, object> dict => dict.TryGetValue(fieldName, out var value) ? value : null,
                JsonObject jo => jo.TryGetPropertyValue(fieldName, out var node) ? node : null,
                JsonElement je when je.ValueKind == JsonValueKind.Object && je.TryGetProperty(fieldName, out var prop) => prop,
                _ => null
            };
        }

        private static object ToPlainValue(object value)
        {
            return value switch
            {
                JsonNode node => node.ToString(),
                JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
                _ => value
            };
        }

        private static int CountSigners(object signers)
        {
            return signers switch
            {
                JsonArray ja => ja.Count,
                JsonElement je when je.ValueKind == JsonValueKind.Array => je.GetArrayLength(),
                ICollection collection => collection.Count,
                IEnumerable<object> enumerable => enumerable.Count(),
                _ => 0
            };
        }

        /// <summary>
        /// Calculates fee for EscrowFinish with Fulfillment.
        /// Formula: 10 drops × (33 + (Fulfillment size in bytes / 16))
        /// </summary>
        private static BigInteger CalculateEscrowFinishFee(Dictionary<string, object> tx, string netFeeDrops)
        {
            decimal fulfillmentBytesSize = Math.Ceiling((decimal)((string)tx["Fulfillment"]).Length / 2);
            decimal multiplier = 33 + (fulfillmentBytesSize / 16);
            var scaled = ScaleValueDecimal(netFeeDrops, multiplier);
            return new BigInteger(Math.Ceiling(scaled));
        }

        /// <summary>
        /// Calculates fee for Batch transactions.
        /// Base fee is multiplied by BATCH_BASE_FEE_MULTIPLIER plus fee for each inner transaction.
        /// </summary>
        private static async Task<BigInteger> CalculateBatchFee(IXrplClient client, Dictionary<string, object> tx, BigInteger baseFee, CancellationToken cancellationToken = default)
        {
            var calculatedFee = baseFee * BATCH_BASE_FEE_MULTIPLIER;
            
            if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
            {
                throw new ValidationException("Batch transaction must have RawTransactions field.");
            }

            IEnumerable<object> items = rawTransactions switch
            {
                JsonArray ja => JsonSerializer.Deserialize<List<object>>(ja.ToJsonString(), XrplJsonOptions.Default)!,
                IEnumerable<object> ie => ie,
                _ => new List<object> { rawTransactions }
            };

            foreach (var inner in items)
            {
                if (!TryGetInnerFieldsAsDict(inner, out var innerTx))
                    throw new ArgumentNullException(nameof(inner), "RawTransaction not found or invalid.");

                calculatedFee += IsReserveFeeTxNeed(innerTx) 
                    ? await FetchReserveFee(client, cancellationToken) 
                    : baseFee;
            }

            return calculatedFee;
        }

        /// <summary>
        /// Counts multisig signers nested inside SponsorSignature (XLS-68).
        /// Mirrors rippled Transactor::calculateBaseFee: only SponsorSignature.Signers
        /// entries add fee units; a single sponsor signature is free.
        /// </summary>
        private static int GetSponsorSignerCount(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("SponsorSignature", out var sponsorSignature) || sponsorSignature == null)
                return 0;

            return CountSigners(GetNestedField(sponsorSignature, "Signers"));
        }

        private static bool TryGetInnerFieldsAsDict(object item, out Dictionary<string, object> dict)
        {
            dict = null!;

            // Приводим к JsonObject максимально рано
            JsonObject entry = item as JsonObject
                ?? JsonNode.Parse(JsonSerializer.Serialize(item, XrplJsonOptions.Default))?.AsObject();
            if (entry == null) return false;

            // Достаём RawTransaction
            JsonNode rawNode = entry["RawTransaction"];
            if (rawNode == null) return false;
            JsonObject raw = rawNode as JsonObject
                ?? JsonNode.Parse(rawNode.ToJsonString())?.AsObject();
            if (raw == null) return false;

            // В словарь
            var tmp = JsonSerializer.Deserialize<Dictionary<string, object>>(raw.ToJsonString(), XrplJsonOptions.Default);
            if (tmp == null) return false;

            dict = tmp;
            return true;
        }
        private static bool IsReserveFeeTxNeed(Dictionary<string, object> tx)
        {
            string txType = $"{tx["TransactionType"]}";
            return txType 
                is nameof(TransactionType.AccountDelete) 
                or nameof(TransactionType.AMMCreate) 
                or nameof(TransactionType.LedgerStateFix);
        }

        public static decimal ScaleValueDecimal(string value, decimal multiplier)
        {
            return decimal.Parse(value, CultureInfo.InvariantCulture) * multiplier;
        }

        public static async Task SetLatestValidatedLedgerSequence(this IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            uint ledgerSequence = await client.GetLedgerIndex(cancellationToken);
            tx.TryAdd("LastLedgerSequence", ledgerSequence + LEDGER_OFFSET);
        }

        public static async Task CheckAccountDeleteBlockers(this IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Validated);
            AccountObjectsRequest request = new AccountObjectsRequest((string)tx["Account"])
            {
                LedgerIndex = index,
                DeletionBlockersOnly = true,
            };
            AccountObjects response = await client.AccountObjects(request, cancellationToken).Typed();
            TaskCompletionSource task = new TaskCompletionSource();
            if (response.AccountObjectList.Count > 0)
            {
                task.TrySetException(new XrplException($"Account {(string)tx["Account"]} cannot be deleted; there are Escrows, PayChannels, RippleStates, or Checks associated with the account."));
            }
            task.TrySetResult();
        }
    }
}