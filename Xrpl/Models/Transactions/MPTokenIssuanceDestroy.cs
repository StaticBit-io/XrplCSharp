#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The MPTokenIssuanceDestroy transaction is used to remove an MPTokenIssuance object
    /// from the directory node in which it is being held, effectively removing the token from the ledger.
    /// </summary>
    public interface IMPTokenIssuanceDestroy : ITransactionCommon
    {
        /// <summary>
        /// Identifies the MPTokenIssuance object to be removed by the transaction.
        /// </summary>
        public string MPTokenIssuanceID { get; set; }
    }

    /// <summary>
    /// The MPTokenIssuanceDestroy transaction removes an MPTokenIssuance object.
    /// </summary>
    public class MPTokenIssuanceDestroy : TransactionRequest, IMPTokenIssuanceDestroy
    {
        /// <summary>
        /// Initializes a new instance of the MPTokenIssuanceDestroy class.
        /// </summary>
        public MPTokenIssuanceDestroy()
        {
            TransactionType = TransactionType.MPTokenIssuanceDestroy;
        }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;
    }

    /// <inheritdoc cref="IMPTokenIssuanceDestroy" />
    public class MPTokenIssuanceDestroyResponse : TransactionResponse, IMPTokenIssuanceDestroy
    {
        #region Implementation of IMPTokenIssuanceDestroy

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;

        #endregion
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of an MPTokenIssuanceDestroy at runtime.
        /// </summary>
        /// <param name="tx">An MPTokenIssuanceDestroy Transaction.</param>
        /// <exception cref="ValidationException">When the MPTokenIssuanceDestroy is Malformed.</exception>
        public static async Task ValidateMPTokenIssuanceDestroy(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("MPTokenIssuanceID", out var issuanceId) || issuanceId is null)
            {
                throw new ValidationException("MPTokenIssuanceDestroy: missing field MPTokenIssuanceID");
            }

            if (issuanceId is not string)
            {
                throw new ValidationException("MPTokenIssuanceDestroy: MPTokenIssuanceID must be a string");
            }
        }
    }
}