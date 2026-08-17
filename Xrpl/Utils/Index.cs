using System;
using System.Text.Json.Nodes;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Keypairs;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;

//https://github.com/XRPLF/xrpl.js/blob/45963b70356f4609781a6396407e2211fd15bcf1/packages/xrpl/src/utils/index.ts

namespace Xrpl.Utils
{
    public static class Utilities
    {
        public static bool IsValidSecret(string secret)
        {
            try
            {
                XrplKeypairs.DeriveKeypair(secret);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    
        public static string Encode(this TransactionRequest transactionOrLedgerEntry)
        {
            return XrplBinaryCodec.Encode(transactionOrLedgerEntry);
        }
    
        public static string EncodeForSigning(this TransactionRequest transaction)
        {
            return XrplBinaryCodec.EncodeForSigning(transaction);
        }
    
        public static string EncodeForSigningClaim(this PaymentChannelClaim paymentChannelClaim)
        {
            return XrplBinaryCodec.EncodeForSigningClaim(paymentChannelClaim);
        }
    
        public static string EncodeForMultiSigning(this TransactionRequest transaction, string signer)
        {
            return XrplBinaryCodec.EncodeForMultiSigning(transaction, signer);
        }
    
        public static JsonNode Decode(string hex)
        {
            return XrplBinaryCodec.Decode(hex);
        }
    
        public static bool IsValidAddress(string address)
        {
            return XrplAddressCodec.IsValidXAddress(address) || XrplCodec.IsValidClassicAddress(address);
        }
    
        /// <summary>
        /// True when the node reported a <c>marker</c>, meaning more pages follow.
        /// </summary>
        /// <remarks>
        /// Read off the raw result rather than a parsed projection: the previous form compared
        /// against <c>Dictionary&lt;string, object&gt;</c>, which the member never was, so it
        /// answered false for every response including paged ones.
        /// </remarks>
        public static bool HasNextPage(this BaseResponse response)
        {
            return response is not null && response.RawResult.HasTopLevelProperty("marker"u8);
        }
    }
}
