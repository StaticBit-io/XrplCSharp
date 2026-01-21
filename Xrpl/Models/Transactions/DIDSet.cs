using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;

// https://xrpl.org/docs/references/protocol/transactions/types/didset

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Creates a new DID ledger entry or updates the fields of an existing one.
    /// The DID (Decentralized Identifier) is associated with the sending account.
    /// </summary>
    public interface IDIDSet : ITransactionCommon
    {
        /// <summary>
        /// The public attestations of identity credentials associated with the DID.
        /// This field is encoded as a hexadecimal string and is limited to 256 bytes.
        /// </summary>
        string Data { get; set; }

        /// <summary>
        /// The DID document for the DID. This field is encoded as a hexadecimal string
        /// and is limited to 256 bytes.
        /// </summary>
        string DIDDocument { get; set; }

        /// <summary>
        /// The Universal Resource Identifier associated with the DID.
        /// This field is encoded as a hexadecimal string and is limited to 256 bytes.
        /// </summary>
        string URI { get; set; }
    }

    /// <inheritdoc cref="IDIDSet" />
    public class DIDSet : TransactionRequest, IDIDSet
    {
        /// <summary>
        /// Initializes a new instance of the DIDSet class.
        /// </summary>
        public DIDSet()
        {
            TransactionType = TransactionType.DIDSet;
        }

        /// <inheritdoc />
        [JsonProperty("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonProperty("DIDDocument")]
        public string DIDDocument { get; set; }

        /// <inheritdoc />
        [JsonProperty("URI")]
        public string URI { get; set; }
    }

    /// <inheritdoc cref="IDIDSet" />
    public class DIDSetResponse : TransactionResponse, IDIDSet
    {
        /// <inheritdoc />
        [JsonProperty("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonProperty("DIDDocument")]
        public string DIDDocument { get; set; }

        /// <inheritdoc />
        [JsonProperty("URI")]
        public string URI { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a DIDSet at runtime.
        /// </summary>
        /// <param name="tx">A DIDSet Transaction.</param>
        /// <exception cref="ValidationException">When the DIDSet is malformed.</exception>
        private const int MaxDIDFieldLength = 512;

        public static async Task ValidateDIDSet(Dictionary<string, dynamic> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            tx.TryGetValue("Data", out var data);
            tx.TryGetValue("DIDDocument", out var didDocument);
            tx.TryGetValue("URI", out var uri);

            bool hasData = data is not null && data is string dataStr && !string.IsNullOrEmpty(dataStr);
            bool hasDocument = didDocument is not null && didDocument is string docStr && !string.IsNullOrEmpty(docStr);
            bool hasUri = uri is not null && uri is string uriStr && !string.IsNullOrEmpty(uriStr);

            if (!hasData && !hasDocument && !hasUri)
            {
                throw new ValidationException("DIDSet: must include at least one of Data, DIDDocument, or URI");
            }

            if (hasData && data is string dataVal && dataVal.Length > MaxDIDFieldLength)
            {
                throw new ValidationException("DIDSet: Data must not exceed 256 bytes (512 hex characters)");
            }

            if (hasDocument && didDocument is string docVal && docVal.Length > MaxDIDFieldLength)
            {
                throw new ValidationException("DIDSet: DIDDocument must not exceed 256 bytes (512 hex characters)");
            }

            if (hasUri && uri is string uriVal && uriVal.Length > MaxDIDFieldLength)
            {
                throw new ValidationException("DIDSet: URI must not exceed 256 bytes (512 hex characters)");
            }
        }
    }
}
