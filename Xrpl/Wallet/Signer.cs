using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Keypairs;


// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/Wallet/signer.ts

namespace Xrpl.Wallet
{
    public class Signer
    {

        /// <summary>
        /// Takes several transactions with Signer fields (in object or blob form) and creates a single transaction with all Signers that then gets signed and returned.
        /// </summary>
        /// <param name="txBlobs">An array of signed Transactions in blob (hex string) form to combine into a single signed Transaction.</param>
        /// <returns>A single signed Transaction which has all Signers from transactions within it.</returns>
        /// <throws>ValidationException if there were no transactions given to sign, the SigningPubKey field is not the empty string, or any transaction is missing a Signers field.</throws>
        public static string Multisign(string[] txBlobs)
        {
            if (txBlobs == null || txBlobs.Length == 0)
            {
                throw new ValidationException("There were 0 transactions to multisign");
            }

            var decodedTransactions = txBlobs.Select(blob => GetDecodedTransaction(blob)).ToArray();

            foreach (var tx in decodedTransactions)
            {
                ValidateMultisignTransaction(tx);
            }

            ValidateTransactionEquivalence(decodedTransactions);
            return XrplBinaryCodec.Encode(GetTransactionWithAllSigners(decodedTransactions));
        }

        /// <summary>
        /// Takes several transactions with Signer fields (in object or blob form) and creates a single transaction with all Signers that then gets signed and returned.
        /// </summary>
        /// <param name="txs">An array of signed Transactions (in object form) to combine into a single signed Transaction.</param>
        /// <returns>A single signed Transaction which has all Signers from transactions within it.</returns>
        /// <throws>ValidationException if there were no transactions given to sign, the SigningPubKey field is not the empty string, or any transaction is missing a Signers field.</throws>
        public static string Multisign(Dictionary<string, dynamic>[] txs)
        {
            if (txs == null || txs.Length == 0)
            {
                throw new ValidationException("There were 0 transactions to multisign");
            }

            var decodedTransactions = txs.Select(tx => GetDecodedTransaction(tx)).ToArray();

            foreach (var tx in decodedTransactions)
            {
                ValidateMultisignTransaction(tx);
            }

            ValidateTransactionEquivalence(decodedTransactions);
            return XrplBinaryCodec.Encode(GetTransactionWithAllSigners(decodedTransactions));
        }

        private static void ValidateMultisignTransaction(Dictionary<string, dynamic> tx)
        {
            if (!tx.ContainsKey("Signers") || tx["Signers"] == null)
            {
                throw new ValidationException("For multisigning all transactions must include a Signers field containing an array of signatures. You may have forgotten to pass the 'forMultisign' parameter when signing.");
            }

            var signers = tx["Signers"];
            int signersCount = 0;
            if (signers is JArray jarr)
                signersCount = jarr.Count;
            else if (signers is Array arr)
                signersCount = arr.Length;
            else if (signers is System.Collections.ICollection col)
                signersCount = col.Count;

            if (signersCount == 0)
            {
                throw new ValidationException("For multisigning all transactions must include a Signers field containing an array of signatures. You may have forgotten to pass the 'forMultisign' parameter when signing.");
            }

            if (!tx.ContainsKey("SigningPubKey") || tx["SigningPubKey"]?.ToString() != "")
            {
                throw new ValidationException("SigningPubKey must be an empty string for all transactions when multisigning.");
            }
        }

        /// <summary>
        /// Creates a signature that can be used to redeem a specific amount of XRP from a payment channel.
        /// </summary>
        /// <param name="wallet">The account that will sign for this payment channel.</param>
        /// <param name="channelID">An id for the payment channel to redeem XRP from.</param>
        /// <param name="amount">The amount in drops to redeem.</param>
        /// <returns>A signature that can be used to redeem a specific amount of XRP from a payment channel.</returns>
        public static string AuthorizeChannel(XrplWallet wallet, string channelID, string amount)
        {
            Dictionary<string, dynamic> json = new Dictionary<string, dynamic>();
            json.Add("channel", channelID);
            json.Add("amount", amount);
            string signatureData = XrplBinaryCodec.EncodeForSigningClaim(json);
            return XrplKeypairs.Sign(signatureData.FromHex(), wallet.PrivateKey);
        }

        /// <summary>
        /// Verifies that the given transaction has a valid signature based on public-key encryption.
        /// </summary>
        /// <param name="tx">A transaction object to verify the signature of. (Can be in object or encoded string format).</param>
        /// <returns>Returns true if tx has a valid signature, and returns false otherwise.</returns>
        public static bool VerifySignature(Dictionary<string, dynamic> tx)
        {
            Dictionary<string, dynamic> decodedTx = GetDecodedTransaction(tx);
            return XrplKeypairs.Verify(
              XrplBinaryCodec.EncodeForSigning(decodedTx).FromHex(),
              decodedTx["TxnSignature"],
              decodedTx["SigningPubKey"]
            );
        }

        /// <summary>
        /// Verifies that the given transaction has a valid signature based on public-key encryption.
        /// </summary>
        /// <param name="tx">A transaction string to verify the signature of.</param>
        /// <returns>Returns true if tx has a valid signature, and returns false otherwise.</returns>
        public static bool VerifySignature(string tx)
        {
            Dictionary<string, dynamic> decodedTx = GetDecodedTransaction(tx);
            return XrplKeypairs.Verify(
              XrplBinaryCodec.EncodeForSigning(decodedTx).FromHex(),
              decodedTx["TxnSignature"],
              decodedTx["SigningPubKey"]
            );
        }

        /// <summary>
        /// The transactions should all be equal except for the 'Signers' field.
        /// </summary>
        /// <param name="transactions">An array of Transactions which are expected to be equal other than 'Signers'.</param>
        /// <throws>ValidationException if the transactions are not equal in any field other than 'Signers'.</throws>
        public static void ValidateTransactionEquivalence(Dictionary<string, dynamic>[] transactions)
        {
            if (transactions.Length < 2)
                return;

            string GetCanonicalEncoding(Dictionary<string, dynamic> tx)
            {
                var clone = new Dictionary<string, dynamic>(tx);
                clone.Remove("Signers");
                return XrplBinaryCodec.Encode(clone);
            }

            string exampleEncoded = GetCanonicalEncoding(transactions[0]);

            for (int i = 1; i < transactions.Length; i++)
            {
                string currentEncoded = GetCanonicalEncoding(transactions[i]);
                if (currentEncoded != exampleEncoded)
                {
                    throw new ValidationException("txJSON is not the same for all signedTransactions");
                }
            }
        }

        /// <summary>
        /// Creates a transaction with all signers from the given transactions, deduped and sorted.
        /// </summary>
        /// <param name="transactions">An array of Transactions with Signers fields.</param>
        /// <returns>A single transaction with all Signers combined, deduped, and sorted.</returns>
        public static Dictionary<string, dynamic> GetTransactionWithAllSigners(Dictionary<string, dynamic>[] transactions)
        {
            var allSigners = new JArray();

            foreach (var tx in transactions)
            {
                if (tx.ContainsKey("Signers") && tx["Signers"] != null)
                {
                    var signers = tx["Signers"];
                    JArray signersArray;

                    if (signers is JArray jarr)
                    {
                        signersArray = jarr;
                    }
                    else
                    {
                        signersArray = JArray.FromObject(signers);
                    }

                    foreach (var signer in signersArray)
                    {
                        allSigners.Add(signer.DeepClone());
                    }
                }
            }

            var sortedSigners = SignerUtilities.DedupeAndSortSigners(allSigners);
            var signersAsList = SignerUtilities.ConvertJTokenToClrType(sortedSigners);

            var finalTx = new Dictionary<string, dynamic>(transactions[0]);
            finalTx["Signers"] = signersAsList;

            return finalTx;
        }

        /// <summary>
        /// If presented in binary form, the Signers array must be sorted based on
        /// the numeric value of the signer addresses, with the lowest value first.
        /// (If submitted as JSON, the submit_multisigned method handles this automatically.)
        /// https://xrpl.org/multi-signing.html.
        /// </summary>
        /// <param name="left">A Signer to compare with.</param>
        /// <param name="right">A Signer to compare with.</param>
        /// <returns>Returns 1 if left > right, 0 if left = right, -1 if left &lt; right.</returns>
        public static int CompareSigners(Dictionary<string, dynamic> left, Dictionary<string, dynamic> right)
        {
            var leftBytes = SignerUtilities.GetAccountIdBytes(left["Signer"]["Account"].ToString());
            var rightBytes = SignerUtilities.GetAccountIdBytes(right["Signer"]["Account"].ToString());
            return SignerUtilities.ByteArrayComparer.Instance.Compare(leftBytes, rightBytes);
        }

        public static Dictionary<string, dynamic> GetDecodedTransaction(Dictionary<string, dynamic>  txOrBlob)
        {
            return XrplBinaryCodec.Decode(XrplBinaryCodec.Encode(txOrBlob)).ToObject<Dictionary<string, dynamic>>();
        }

        public static Dictionary<string, dynamic> GetDecodedTransaction(string txOrBlob)
        {
            return XrplBinaryCodec.Decode(txOrBlob).ToObject<Dictionary<string, dynamic>>();
        }
    }
}