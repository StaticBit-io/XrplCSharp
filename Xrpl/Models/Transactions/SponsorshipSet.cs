using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Values of the common SponsorFlags transaction field (XLS-68):
    /// what a sponsor covers in a sponsored transaction.
    /// </summary>
    [Flags]
    public enum SponsorCoverage : uint
    {
        /// <summary>The sponsor pays the transaction fee.</summary>
        spfSponsorFee = 1,

        /// <summary>The sponsor provides the reserve for objects created by the transaction.</summary>
        spfSponsorReserve = 2,
    }

    [Flags]
    public enum SponsorshipSetFlags : uint
    {
        /// <summary>
        /// Future sponsored fee payments require the sponsor's co-signature (SponsorSignature).
        /// </summary>
        tfSponsorshipSetRequireSignForFee = 0x00010000,

        /// <summary>
        /// Clear the require-signature requirement for sponsored fees.
        /// </summary>
        tfSponsorshipClearRequireSignForFee = 0x00020000,

        /// <summary>
        /// Future sponsored reserve allocations require the sponsor's co-signature.
        /// </summary>
        tfSponsorshipSetRequireSignForReserve = 0x00040000,

        /// <summary>
        /// Clear the require-signature requirement for sponsored reserves.
        /// </summary>
        tfSponsorshipClearRequireSignForReserve = 0x00080000,

        /// <summary>
        /// Delete the Sponsorship ledger object.
        /// </summary>
        tfDeleteObject = 0x00100000,
    }

    /// <summary>
    /// The SponsorshipSet transaction creates or updates a Sponsorship relationship,
    /// allowing the sponsor to pay transaction fees and/or reserves on behalf of the sponsee.
    /// </summary>
    /// <remarks>Requires the Sponsor amendment (XLS-68). This feature is in draft and subject to change.</remarks>
    public interface ISponsorshipSet : ITransactionCommon
    {
        /// <summary>
        /// The account being sponsored. Present when the sponsor submits the transaction.
        /// </summary>
        string Sponsee { get; set; }

        /// <summary>
        /// The sponsoring account. Present when the sponsee submits the transaction
        /// (the sponsor then co-signs via SponsorSignature).
        /// </summary>
        string CounterpartySponsor { get; set; }

        /// <summary>
        /// Signed change applied to the FeeAmount held by the Sponsorship object —
        /// XRP the sponsor adds to (positive) or reclaims from (negative) the fee budget.
        /// Must be a non-zero XRP amount, and positive when the object is being created.
        /// </summary>
        Currency FeeAmountDelta { get; set; }

        /// <summary>
        /// The maximum fee per transaction the sponsor is willing to cover.
        /// </summary>
        Currency MaxFee { get; set; }

        /// <summary>
        /// Signed change applied to the RemainingOwnerCount held by the Sponsorship
        /// object — owner-reserve slots the sponsor adds (positive) or withdraws
        /// (negative). Must be non-zero, and positive when the object is being created.
        /// </summary>
        int? RemainingOwnerCountDelta { get; set; }
    }

    /// <inheritdoc cref="ISponsorshipSet" />
    public class SponsorshipSet : TransactionRequest, ISponsorshipSet
    {
        public SponsorshipSet()
        {
            TransactionType = TransactionType.SponsorshipSet;
        }

        /// <summary>
        /// Typed view over the base Flags value (see <see cref="SponsorshipSetFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new SponsorshipSetFlags? Flags
        {
            get => base.Flags.HasValue ? (SponsorshipSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("Sponsee")]
        public string Sponsee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CounterpartySponsor")]
        public string CounterpartySponsor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("FeeAmountDelta")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency FeeAmountDelta { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MaxFee")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency MaxFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("RemainingOwnerCountDelta")]
        public int? RemainingOwnerCountDelta { get; set; }
    }

    /// <inheritdoc cref="ISponsorshipSet" />
    public class SponsorshipSetResponse : TransactionResponse, ISponsorshipSet
    {
        /// <summary>
        /// Typed view over the base Flags value (see <see cref="SponsorshipSetFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new SponsorshipSetFlags? Flags
        {
            get => base.Flags.HasValue ? (SponsorshipSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("Sponsee")]
        public string Sponsee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CounterpartySponsor")]
        public string CounterpartySponsor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("FeeAmountDelta")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency FeeAmountDelta { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MaxFee")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency MaxFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("RemainingOwnerCountDelta")]
        public int? RemainingOwnerCountDelta { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateSponsorshipSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            bool hasSponsee = tx.TryGetValue("Sponsee", out var sponsee) && sponsee is string;
            bool hasCounterpartySponsor = tx.TryGetValue("CounterpartySponsor", out var cps) && cps is string;

            // Exactly one side of the relationship identifies the other party:
            // the sponsor names the Sponsee, or the sponsee names the CounterpartySponsor.
            if (hasSponsee == hasCounterpartySponsor)
                throw new ValidationException("SponsorshipSet: exactly one of Sponsee or CounterpartySponsor must be present");

            uint flags = ExtractFlags(tx);
            bool isDelete = (flags & (uint)SponsorshipSetFlags.tfDeleteObject) != 0;

            bool hasFeeAmountDelta = tx.TryGetValue("FeeAmountDelta", out var feeDelta) && feeDelta is not null;
            bool hasRemainingOwnerCountDelta = tx.TryGetValue("RemainingOwnerCountDelta", out var rocDelta) && rocDelta is not null;
            bool hasMaxFee = tx.TryGetValue("MaxFee", out var maxFee) && maxFee is not null;

            // rippled SponsorshipSet::preflight: a delete carries no modification fields
            if (isDelete && (hasFeeAmountDelta || hasRemainingOwnerCountDelta || hasMaxFee))
                throw new ValidationException("SponsorshipSet: tfDeleteObject cannot be combined with FeeAmountDelta, RemainingOwnerCountDelta or MaxFee");

            if (hasRemainingOwnerCountDelta)
            {
                // The field is serialized as Int32 and may be negative, but never zero (temINVALID)
                if (!Common.TryGetInt32(rocDelta, out int delta))
                    throw new ValidationException("SponsorshipSet: RemainingOwnerCountDelta must be a number");

                if (delta == 0)
                    throw new ValidationException("SponsorshipSet: RemainingOwnerCountDelta must not be zero");
            }

            if (hasFeeAmountDelta)
            {
                // rippled SponsorshipSet::preflight: a non-zero XRP amount, so drops
                // as a string — an issued currency object is temBAD_AMOUNT. The delta
                // may be negative, which reclaims budget from the Sponsorship object.
                if (feeDelta is not string drops ||
                    !long.TryParse(drops, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long dropsValue))
                    throw new ValidationException("SponsorshipSet: FeeAmountDelta must be an XRP amount in drops");

                if (dropsValue == 0)
                    throw new ValidationException("SponsorshipSet: FeeAmountDelta must not be zero");
            }

            const uint feePair = (uint)(SponsorshipSetFlags.tfSponsorshipSetRequireSignForFee | SponsorshipSetFlags.tfSponsorshipClearRequireSignForFee);
            const uint reservePair = (uint)(SponsorshipSetFlags.tfSponsorshipSetRequireSignForReserve | SponsorshipSetFlags.tfSponsorshipClearRequireSignForReserve);
            if ((flags & feePair) == feePair || (flags & reservePair) == reservePair)
                throw new ValidationException("SponsorshipSet: cannot set and clear the same require-signature flag");
        }
    }
}
