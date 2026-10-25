using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.BinaryCodec.Util;
using Xrpl.Client.Exceptions;

using static Xrpl.AddressCodec.Utils;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/hashes/hashLedger.ts

namespace Xrpl.Utils.Hashes
{
    public interface HashLedgerHeaderOptions
    {
        public bool? ComputeTreeHashes { get; set; }

    }

    public class HashLedger
    {
        private const uint TfInnerBatchTxn = (uint)Xrpl.Models.Enums.XrplGlobalFlags.tfInnerBatchTxn;

        // Any one of these means someone has signed or is signing the blob. xrpl.js checks the
        // first three plus tfInnerBatchTxn; the co-signature fields are added because the wallet
        // hashes blobs that carry only those while they travel between signers (LoanSet
        // counterparty, sponsor, BatchSigners collected before the submitter signs the outer
        // transaction), and refusing them would break co-signing.
        private static readonly string[] SignatureFields =
        {
            "TxnSignature",
            "Signers",
            "SigningPubKey",
            "BatchSigners",
            "CounterpartySignature",
            "SponsorSignature",
        };

        /// <summary>
        /// Hash of a signed transaction blob: its transaction ID.
        /// </summary>
        /// <param name="tx">Hex of the serialized transaction.</param>
        /// <exception cref="ValidationException">The transaction carries no signature fields at all.</exception>
        public static string HashSignedTx(string tx)
        {
            string txBlob = tx;
            EnsureSigned(XrplBinaryCodec.Decode(tx));
            return B16.Encode(Sha512.Half(input: txBlob.FromHex(), prefix: (uint)Xrpl.BinaryCodec.Hashing.HashPrefix.TransactionId));
        }

        /// <summary>
        /// Hash of a signed transaction: its transaction ID.
        /// </summary>
        /// <param name="tx">The transaction JSON.</param>
        /// <exception cref="ValidationException">The transaction carries no signature fields at all.</exception>
        public static string HashSignedTx(JsonNode tx)
        {
            string txBlob = XrplBinaryCodec.Encode(tx);
            EnsureSigned(tx);
            return B16.Encode(Sha512.Half(input: txBlob.FromHex(), prefix: (uint)Xrpl.BinaryCodec.Hashing.HashPrefix.TransactionId));
        }

        private static void EnsureSigned(JsonNode tx)
        {
            if (tx is not JsonObject txObject)
                throw new ValidationException("The transaction must be a JSON object to hash it.");

            foreach (string field in SignatureFields)
            {
                if (txObject.ContainsKey(field))
                    return;
            }

            if (txObject["Flags"] is JsonValue flags && flags.TryGetValue(out uint value) && (value & TfInnerBatchTxn) != 0)
                return;

            throw new ValidationException("The transaction must be signed to hash it.");
        }
    }
}
