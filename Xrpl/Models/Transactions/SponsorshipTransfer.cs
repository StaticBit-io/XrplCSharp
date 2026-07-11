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

            if (tx.TryGetValue("Sponsee", out var sponsee) && sponsee is not string)
                throw new ValidationException("SponsorshipTransfer: invalid Sponsee");
        }
    }
}
