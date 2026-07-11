using System;
using System.Collections.Generic;
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
        /// The amount of fees the sponsor commits to cover.
        /// </summary>
        Currency FeeAmount { get; set; }

        /// <summary>
        /// The maximum fee per transaction the sponsor is willing to cover.
        /// </summary>
        Currency MaxFee { get; set; }

        /// <summary>
        /// The number of owner-reserve slots the sponsor commits to cover.
        /// </summary>
        uint? RemainingOwnerCount { get; set; }
    }

    /// <inheritdoc cref="ISponsorshipSet" />
    public class SponsorshipSet : TransactionRequest, ISponsorshipSet
    {
        public SponsorshipSet()
        {
            TransactionType = TransactionType.SponsorshipSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("Sponsee")]
        public string Sponsee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CounterpartySponsor")]
        public string CounterpartySponsor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("FeeAmount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency FeeAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MaxFee")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency MaxFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("RemainingOwnerCount")]
        public uint? RemainingOwnerCount { get; set; }
    }

    /// <inheritdoc cref="ISponsorshipSet" />
    public class SponsorshipSetResponse : TransactionResponse, ISponsorshipSet
    {
        /// <inheritdoc />
        [JsonPropertyName("Sponsee")]
        public string Sponsee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CounterpartySponsor")]
        public string CounterpartySponsor { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("FeeAmount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency FeeAmount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MaxFee")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency MaxFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("RemainingOwnerCount")]
        public uint? RemainingOwnerCount { get; set; }
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

            if (tx.TryGetValue("RemainingOwnerCount", out var roc) && roc is not uint && roc is not long && roc is not int)
                throw new ValidationException("SponsorshipSet: invalid RemainingOwnerCount");
        }
    }
}
