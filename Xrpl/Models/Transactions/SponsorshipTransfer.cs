using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    [Flags]
    public enum SponsorshipTransferFlags : uint
    {
        /// <summary>
        /// End the sponsorship of the given object or account reserve;
        /// the reserve obligation returns to the owner.
        /// </summary>
        tfSponsorshipEnd = 0x00010000,

        /// <summary>
        /// Take over sponsorship of an existing unsponsored object.
        /// </summary>
        tfSponsorshipCreate = 0x00020000,

        /// <summary>
        /// Reassign an existing sponsorship to the submitting sponsor.
        /// </summary>
        tfSponsorshipReassign = 0x00040000,
    }

    /// <summary>
    /// The SponsorshipTransfer transaction ends, creates or reassigns the sponsorship
    /// of a ledger object or of an account reserve.
    /// </summary>
    /// <remarks>Requires the Sponsor amendment (XLS-68). This feature is in draft and subject to change.</remarks>
    public interface ISponsorshipTransfer : ITransactionCommon
    {
        /// <summary>
        /// The ledger object whose sponsorship is being transferred.
        /// When absent, the transfer applies to the account-level sponsorship.
        /// </summary>
        string ObjectID { get; set; }

        /// <summary>
        /// The sponsored account, when the transaction is submitted by the sponsor.
        /// </summary>
        string Sponsee { get; set; }
    }

    /// <inheritdoc cref="ISponsorshipTransfer" />
    public class SponsorshipTransfer : TransactionRequest, ISponsorshipTransfer
    {
        public SponsorshipTransfer()
        {
            TransactionType = TransactionType.SponsorshipTransfer;
        }

        /// <summary>
        /// Typed view over the base Flags value (see <see cref="SponsorshipTransferFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new SponsorshipTransferFlags? Flags
        {
            get => base.Flags.HasValue ? (SponsorshipTransferFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("ObjectID")]
        public string ObjectID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Sponsee")]
        public string Sponsee { get; set; }
    }

    /// <inheritdoc cref="ISponsorshipTransfer" />
    public class SponsorshipTransferResponse : TransactionResponse, ISponsorshipTransfer
    {
        /// <summary>
        /// Typed view over the base Flags value (see <see cref="SponsorshipTransferFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new SponsorshipTransferFlags? Flags
        {
            get => base.Flags.HasValue ? (SponsorshipTransferFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("ObjectID")]
        public string ObjectID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Sponsee")]
        public string Sponsee { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateSponsorshipTransfer(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (tx.TryGetValue("ObjectID", out var objectId) && objectId is not string)
                throw new ValidationException("SponsorshipTransfer: invalid ObjectID");

            bool hasSponsee = tx.TryGetValue("Sponsee", out var sponsee);
            if (hasSponsee && sponsee is not string)
                throw new ValidationException("SponsorshipTransfer: invalid Sponsee");

            // Mirror of rippled SponsorshipTransfer::preflight
            uint flags = ExtractFlags(tx);
            const uint transferFlags = (uint)(SponsorshipTransferFlags.tfSponsorshipCreate
                | SponsorshipTransferFlags.tfSponsorshipReassign
                | SponsorshipTransferFlags.tfSponsorshipEnd);
            if (System.Numerics.BitOperations.PopCount(flags & transferFlags) != 1)
                throw new ValidationException("SponsorshipTransfer: exactly one of tfSponsorshipCreate, tfSponsorshipReassign or tfSponsorshipEnd must be set");

            bool hasSponsorField = tx.TryGetValue("Sponsor", out var sponsorField);
            if (hasSponsorField && sponsorField is not string)
                throw new ValidationException("SponsorshipTransfer: invalid Sponsor");
            bool hasSponsor = hasSponsorField && sponsorField is string;
            bool isCreateOrReassign = (flags & (uint)(SponsorshipTransferFlags.tfSponsorshipCreate | SponsorshipTransferFlags.tfSponsorshipReassign)) != 0;

            if (isCreateOrReassign)
            {
                if (!hasSponsor)
                    throw new ValidationException("SponsorshipTransfer: Sponsor must be present when creating or reassigning sponsorship");
                if (hasSponsee)
                    throw new ValidationException("SponsorshipTransfer: Sponsee must not be present when creating or reassigning sponsorship");
            }
            else // tfSponsorshipEnd
            {
                if (hasSponsor)
                    throw new ValidationException("SponsorshipTransfer: Sponsor must not be present when ending sponsorship");
                if (hasSponsee && tx.TryGetValue("Account", out var account) &&
                    string.Equals(sponsee as string, account as string, StringComparison.Ordinal))
                {
                    throw new ValidationException("SponsorshipTransfer: Sponsee must not be the same as Account");
                }
            }
        }

        private static uint ExtractFlags(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("Flags", out var flagsObj) || flagsObj is null)
                return 0;
            if (!Common.TryGetUInt32(flagsObj, out uint flags))
                throw new ValidationException("Invalid Flags value");
            return flags;
        }
    }
}
