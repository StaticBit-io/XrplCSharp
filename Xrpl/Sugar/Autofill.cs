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
using Xrpl.Models.Enums;
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

            bool hasTT = tx.TryGetValue("TransactionType", out var tt);
            string txType = $"{tt}";

            // An explicit 0 opts out of an expiry: the field is dropped and not filled in.
            bool needsLastLedgerSequence = !tx.TryGetValue("LastLedgerSequence", out var lastLedgerValue);
            if (lastLedgerValue is 0u or 0UL or 0L or 0)
            {
                tx.Remove("LastLedgerSequence");
            }

            // Batch normalisation rewrites RawTransactions and Sequence, and the Batch fee is
            // counted from RawTransactions, so it runs on its own before anything reads tx.
            if (txType == "Batch")
            {
                await client.NormalizeBatchTransaction(tx, cancellationToken);
            }

            // The fetches below run concurrently and only read tx; the results are written after
            // WhenAll. Writing from each continuation, as this used to, mutated one Dictionary from
            // several thread-pool threads at once (no synchronization context in a console or
            // ASP.NET Core host), which can lose a field or corrupt the dictionary.
            // A transaction that consumes a Ticket must carry Sequence 0 (rippled rejects a non-zero
            // Sequence next to TicketSequence with temSEQ_AND_TICKET), so the account sequence is
            // not fetched. xrpl.js fills the account sequence here regardless; its Batch-inner path
            // skips instead, which would leave the required Sequence field out.
            bool usesTicket = tx.TryGetValue("TicketSequence", out var ticketSequence) && ticketSequence != null;
            if (usesTicket && !tx.ContainsKey("Sequence") && txType != "Batch")
            {
                tx["Sequence"] = 0u;
            }
            Task<uint> sequenceTask = !tx.ContainsKey("Sequence") && txType != "Batch"
                ? client.FetchNextValidSequenceNumber((string)tx["Account"], cancellationToken)
                : null;
            Task<string> feeTask = !tx.ContainsKey("Fee")
                ? client.ComputeFeePerTransactionType(tx, signersCount ?? 0, cancellationToken)
                : null;
            Task<uint> lastLedgerSequenceTask = needsLastLedgerSequence
                ? client.FetchLastLedgerSequence(cancellationToken)
                : null;

            Task[] pending = new Task[] { sequenceTask, feeTask, lastLedgerSequenceTask }
                .Where(task => task != null)
                .ToArray();
            await Task.WhenAll(pending);

            if (sequenceTask != null)
            {
                tx.TryAdd("Sequence", sequenceTask.Result);
            }
            if (feeTask != null)
            {
                tx.TryAdd("Fee", feeTask.Result);
            }
            if (lastLedgerSequenceTask != null)
            {
                tx.TryAdd("LastLedgerSequence", lastLedgerSequenceTask.Result);
            }
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
                if (tField != null && (!Xrpl.Models.Transactions.Common.TryGetUInt32(tField, out uint explicitTag) || explicitTag != classicAccount.Tag))
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
            uint sequence = await client.FetchNextValidSequenceNumber((string)tx["Account"], cancellationToken);
            tx.TryAdd("Sequence", sequence);
            return sequence;
        }

        private static async Task<uint> FetchNextValidSequenceNumber(this IXrplClient client, string account, CancellationToken cancellationToken)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Current);
            AccountInfoRequest request = new AccountInfoRequest(account) { LedgerIndex = index };
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

        /// <summary>
        /// The owner reserve scaled by the load factor, rounded up to whole drops. rippled
        /// Transactor::checkFee compares the fee paid with scaleFeeLoad(calculateBaseFee), and for
        /// a reserve-priced type calculateBaseFee is the reserve, so under load the reserve at face
        /// value is refused with telINSUF_FEE_P. The fee cushion is not applied: at load factor 1
        /// it would charge every AccountDelete a fifth of a reserve more than the node requires.
        /// </summary>
        private static async Task<BigInteger> FetchLoadScaledReserveFee(IXrplClient client, decimal loadFactor, CancellationToken cancellationToken)
        {
            BigInteger reserve = await FetchReserveFee(client, cancellationToken);
            return loadFactor <= 1m
                ? reserve
                : new BigInteger(Math.Ceiling((decimal)reserve * loadFactor));
        }

        public static async Task CalculateFeePerTransactionType(this IXrplClient client, Dictionary<string, object> tx, int signersCount = 0, CancellationToken cancellationToken = default)
        {
            string fee = await client.ComputeFeePerTransactionType(tx, signersCount, cancellationToken);
            tx.TryAdd("Fee", fee);
        }

        /// <summary>
        /// Computes the Fee in drops without writing it. Reads TransactionType and the fields the
        /// per-type rules depend on (RawTransactions, Fulfillment, CounterpartySignature,
        /// Counterparty, SponsorSignature, Flags, LoanID, Amount) — never Sequence, Fee or
        /// LastLedgerSequence, which Autofill fetches concurrently.
        /// </summary>
        private static async Task<string> ComputeFeePerTransactionType(this IXrplClient client, Dictionary<string, object> tx, int signersCount, CancellationToken cancellationToken)
        {
            (string netFeeXRP, decimal loadFactor) = await GetFeeXrpSugar.FetchFeeXrp(client, null, cancellationToken);
            var netFeeDrops = XrpConversion.XrpToDrops(netFeeXRP);
            var baseFee = new BigInteger(Math.Floor(decimal.Parse(netFeeDrops, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)));
            
            // Devnet returns fees ~12x lower than mainnet. Detect by checking if baseFee < 10 drops.
            if (baseFee < 10)
            {
                baseFee *= DEVNET_FEE_CORRECTION_MULTIPLIER;
            }

            var transactionType = (string)tx["TransactionType"];
            var calculatedFee = await CalculateTransactorFee(client, tx, transactionType, baseFee, netFeeDrops, loadFactor, signersCount, IsInnerBatchTxn(tx), cancellationToken);

            BigInteger totalFee;
            if (!string.IsNullOrWhiteSpace(client.maxFeeXRP))
            {
                var maxFeeBI = new BigInteger(GetFeeXrpSugar.MaxFeeDrops(client.maxFeeXRP));
                // The cap bounds what the load factor adds, never the owner reserve itself: each
                // reserve charge (the transaction's own, or one per reserve-priced Batch inner) is
                // allowed on top of it. Capped whole, an AccountDelete, or a Batch carrying an
                // AMMCreate, would fall below rippled's minimum wherever the reserve increment is
                // at or above the cap (a standalone genesis charges 2 XRP).
                int reserveCharges = IsReserveFeeTxNeed(tx) ? 1
                    : transactionType == "Batch" ? CountReservePricedInners(tx)
                    : 0;
                BigInteger reserveAllowance = reserveCharges > 0
                    ? reserveCharges * await FetchReserveFee(client, cancellationToken)
                    : BigInteger.Zero;
                totalFee = BigInteger.Min(calculatedFee, maxFeeBI + reserveAllowance);
            }
            else
            {
                totalFee = calculatedFee;
            }
            return totalFee.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The fee one transaction's own transactor charges, before the maxFeeXRP cap. Batch
        /// prices each inner transaction through this too, so an inner costs what it would cost
        /// on its own.
        /// </summary>
        /// <param name="isInner">
        /// The transaction is a Batch inner: it will never carry signatures of its own, so a
        /// lookup that guesses signatures still to come must not run for it.
        /// </param>
        private static async Task<BigInteger> CalculateTransactorFee(
            IXrplClient client,
            Dictionary<string, object> tx,
            string transactionType,
            BigInteger baseFee,
            string netFeeDrops,
            decimal loadFactor,
            int signersCount,
            bool isInner,
            CancellationToken cancellationToken)
        {
            var calculatedFee = await CalculateBaseFeeForType(client, tx, transactionType, baseFee, netFeeDrops, loadFactor, isInner, cancellationToken);
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

            return calculatedFee;
        }

        private static async Task<BigInteger> CalculateBaseFeeForType(
            IXrplClient client, 
            Dictionary<string, object> tx, 
            string transactionType, 
            BigInteger baseFee, 
            string netFeeDrops,
            decimal loadFactor,
            bool isInner,
            CancellationToken cancellationToken = default)
        {
            return transactionType switch
            {
                "EscrowFinish" when tx.TryGetValue("Fulfillment", out _) => CalculateEscrowFinishFee(tx, netFeeDrops),
                "Batch" => await CalculateBatchFee(client, tx, baseFee, netFeeDrops, loadFactor, cancellationToken),
                nameof(TransactionType.LoanSet) => await CalculateLoanSetFee(client, tx, baseFee, isInner, cancellationToken),
                _ when IsConfidentialMPTTx(transactionType) => baseFee * (1 + CONFIDENTIAL_FEE_MULTIPLIER),
                _ when IsReserveFeeTxNeed(tx) => await FetchLoadScaledReserveFee(client, loadFactor, cancellationToken),
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
                or nameof(TransactionType.ConfidentialMPTClawback)
                or nameof(TransactionType.ConfidentialMPTMirrorUpdate)
                or nameof(TransactionType.ConfidentialMPTHolderKeyUpdate);
        }

        /// <summary>
        /// Calculates fee for LoanSet, which charges one extra base fee per counterparty signature
        /// (rippled LoanSet::calculateBaseFee counts CounterpartySignature.Signers, or the single
        /// signature when present).
        /// </summary>
        /// <remarks>
        /// When the counterparty has not signed yet — the usual case during autofill — the signature
        /// count is unknown, so the counterparty's signer list size is used to avoid underpaying.
        /// A Batch inner is the exception: its CounterpartySignature stays absent, since the
        /// counterparty authorises through its BatchSigner, which the Batch already charges for.
        /// rippled then counts zero signatures, and so does this.
        /// </remarks>
        private static async Task<BigInteger> CalculateLoanSetFee(IXrplClient client, Dictionary<string, object> tx, BigInteger baseFee, bool isInner, CancellationToken cancellationToken = default)
        {
            int counterpartySigners = GetCounterpartySignerCount(tx);
            if (counterpartySigners == 0 && !isInner)
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

            if (loan.PeriodicPayment is not { } periodic
                || LoanPayments.RoundUpToAsset(periodic, integralAsset, loan.LoanScale ?? 0) is not { } roundedPayment
                || roundedPayment <= 0)
                return BigInteger.One;

            decimal serviceFee = 0m;
            if (loan.LoanServiceFee is { } fee)
                fee.TryToDecimal(out serviceFee);
            if (serviceFee > decimal.MaxValue - roundedPayment)
                return BigInteger.One;
            decimal regularPayment = roundedPayment + serviceFee;
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

        private static bool IsInnerBatchTxn(Dictionary<string, object> tx)
        {
            return (ParseTransactionFlags(tx) & (uint)XrplGlobalFlags.tfInnerBatchTxn) != 0;
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
                ulong ul when ul <= uint.MaxValue => (uint)ul,
                // Any flags enum, not only LoanPayFlags: a LoanSetFlags value carrying
                // tfInnerBatchTxn read as 0 would price a Batch inner as a top-level LoanSet.
                Enum e => unchecked((uint)Convert.ToInt64(e, CultureInfo.InvariantCulture)),
                JsonValue jv when jv.TryGetValue(out uint u) => u,
                JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetUInt32(out uint u) => u,
                string s when uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u) => u,
                _ => 0
            };
        }

        /// <summary>
        /// Parses an amount value of the transaction, written as a decimal string.
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
        /// Calculates fee for EscrowFinish with Fulfillment, following rippled
        /// EscrowFinish::calculateBaseFee: fee × (33 + floor(Fulfillment size in bytes / 16)).
        /// </summary>
        private static BigInteger CalculateEscrowFinishFee(Dictionary<string, object> tx, string netFeeDrops)
        {
            // ToString rather than a cast: an inner transaction of a Batch reaches here
            // deserialized from JSON, not as the string a caller wrote.
            int fulfillmentBytesSize = ($"{tx["Fulfillment"]}".Length + 1) / 2;
            // Integer division, as rippled does: a decimal quotient charged up to one base fee
            // more than the node requires whenever the size was not a multiple of 16.
            decimal multiplier = 33 + (fulfillmentBytesSize / 16);
            var scaled = ScaleValueDecimal(netFeeDrops, multiplier);
            return new BigInteger(Math.Ceiling(scaled));
        }

        /// <summary>
        /// Calculates fee for Batch transactions, following rippled Batch::calculateBaseFeeImpl:
        /// one base fee for batch processing, one for the outer transaction itself, each inner
        /// transaction priced by its own transactor, and one base fee per BatchSigners signature.
        /// The outer's own multisig signers are added by the caller, as for any transaction.
        /// </summary>
        /// <remarks>
        /// BatchSigners are usually collected after autofill. Each signature already present is
        /// counted as rippled counts it (one, or every nested Signers entry); each required
        /// account that has not signed yet is counted as one signature. A participant that will
        /// multi-sign its BatchSigner needs a Fee set by hand.
        /// </remarks>
        private static async Task<BigInteger> CalculateBatchFee(IXrplClient client, Dictionary<string, object> tx, BigInteger baseFee, string netFeeDrops, decimal loadFactor, CancellationToken cancellationToken = default)
        {
            var calculatedFee = baseFee * 2;

            if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
            {
                throw new ValidationException("Batch transaction must have RawTransactions field.");
            }

            foreach (var inner in RawTransactionItems(rawTransactions))
            {
                if (!TryGetInnerFieldsAsDict(inner, out var innerTx))
                    throw new ArgumentNullException(nameof(inner), "RawTransaction not found or invalid.");

                string innerType = innerTx.TryGetValue("TransactionType", out var innerTypeValue) ? $"{innerTypeValue}" : null;
                if (string.IsNullOrWhiteSpace(innerType))
                    throw new ValidationException("Each RawTransaction must contain TransactionType.");
                // rippled refuses a Batch nested in a Batch; refusing it here also keeps the
                // pricing from recursing.
                if (innerType == "Batch")
                    throw new ValidationException("A Batch transaction cannot contain an inner Batch.");

                // Inner transactions carry no signatures of their own: their signers sign the
                // outer batch as BatchSigners, which are counted below.
                calculatedFee += await CalculateTransactorFee(client, innerTx, innerType, baseFee, netFeeDrops, loadFactor, 0, isInner: true, cancellationToken);
            }

            calculatedFee += baseFee * CountBatchSignatures(tx);

            return calculatedFee;
        }

        private static IEnumerable<object> RawTransactionItems(object rawTransactions)
        {
            return rawTransactions switch
            {
                JsonArray ja => JsonSerializer.Deserialize<List<object>>(ja.ToJsonString(), XrplJsonOptions.Default)!,
                IEnumerable<object> ie => ie,
                _ => new List<object> { rawTransactions }
            };
        }

        /// <summary>
        /// Number of inner transactions of a Batch priced at the owner reserve. Called after
        /// <see cref="CalculateBatchFee"/> has validated RawTransactions.
        /// </summary>
        private static int CountReservePricedInners(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
                return 0;

            int count = 0;
            foreach (var inner in RawTransactionItems(rawTransactions))
            {
                if (TryGetInnerFieldsAsDict(inner, out var innerTx)
                    && innerTx.ContainsKey("TransactionType")
                    && IsReserveFeeTxNeed(innerTx))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Number of BatchSigners signatures the batch will carry: every signature already present
        /// (rippled counts a TxnSignature as one and a nested Signers array entry by entry), plus
        /// one for each required account that has no BatchSigner yet.
        /// </summary>
        private static int CountBatchSignatures(Dictionary<string, object> tx)
        {
            var signedAccounts = new HashSet<string>(StringComparer.Ordinal);
            int signatures = 0;

            if (tx.TryGetValue("BatchSigners", out var batchSigners) && batchSigners != null)
            {
                JsonArray signersArray = batchSigners as JsonArray
                    ?? JsonNode.Parse(JsonSerializer.Serialize(batchSigners, XrplJsonOptions.Default)) as JsonArray;
                foreach (JsonNode entry in signersArray ?? new JsonArray())
                {
                    if (entry?["BatchSigner"] is not JsonObject signer)
                        continue;

                    if (signer["TxnSignature"] != null)
                        signatures += 1;
                    else if (signer["Signers"] is JsonArray nested)
                        signatures += nested.Count;
                    else
                        continue;

                    if (signer["Account"]?.GetValueKind() == JsonValueKind.String)
                        signedAccounts.Add(signer["Account"].GetValue<string>());
                }
            }

            signatures += tx.GetBatchSignerAccounts().Raw.Count(account => !signedAccounts.Contains(account));
            return signatures;
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

            // Convert to a JsonObject as early as possible
            JsonObject entry = item as JsonObject
                ?? JsonNode.Parse(JsonSerializer.Serialize(item, XrplJsonOptions.Default))?.AsObject();
            if (entry == null) return false;

            // Fetch RawTransaction
            JsonNode rawNode = entry["RawTransaction"];
            if (rawNode == null) return false;
            JsonObject raw = rawNode as JsonObject
                ?? JsonNode.Parse(rawNode.ToJsonString())?.AsObject();
            if (raw == null) return false;

            // Into the dictionary
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
            uint lastLedgerSequence = await client.FetchLastLedgerSequence(cancellationToken);
            tx.TryAdd("LastLedgerSequence", lastLedgerSequence);
        }

        private static async Task<uint> FetchLastLedgerSequence(this IXrplClient client, CancellationToken cancellationToken)
        {
            uint ledgerSequence = await client.GetLedgerIndex(cancellationToken);
            return ledgerSequence + LEDGER_OFFSET;
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
            if (response.AccountObjectList?.Count > 0)
            {
                throw new XrplException($"Account {(string)tx["Account"]} cannot be deleted; there are Escrows, PayChannels, RippleStates, or Checks associated with the account.");
            }
        }
    }
}