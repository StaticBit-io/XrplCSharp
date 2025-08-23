using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.BinaryCodec.Types;
using Xrpl.BinaryCodec.Util;


// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-binary-codec/src/index.ts

namespace Xrpl.BinaryCodec
{
    public class XrplBinaryCodec
    {
        static uint PAYMENT_CHANNEL_CLAIM_PREFIX = 0x434C4D00u;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="binary"></param>
        /// <returns>JToken</returns>
        public static JToken Decode(string binary)
        {
            var stobject = StObject.FromHex(binary);
            return stobject.ToJson();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="token"></param>
        /// <returns>string</returns>
        public static string Encode(JToken token)
        {
            return SerializeJson(token);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="json"></param>
        /// <returns>string</returns>
        public static string Encode(object json)
        {
            JToken token = JToken.FromObject(json);
            return Encode(token);
        }

        /// <summary>
        /// Encode a transaction into binary format in preparation for signing. (Only encodes fields that are intended to be signed.)
        /// </summary>
        /// <param name="json"></param>
        /// <returns>string</returns>
        public static string EncodeForSigning(object json)
        {
            JToken token = JToken.FromObject(json);
            return SerializeJson(token, HashPrefix.TransactionSig.Bytes(), null, true);
        }

        /// <summary>
        /// Encode a `payment channel <a href="https://xrpl.org/payment-channels.html">here</a>`_ Claim to be signed.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns>string</returns> The binary-encoded claim, ready to be signed.
        public static string EncodeForSigningClaim(object obj)
        {
            JToken json = JToken.FromObject(obj);

            byte[] prefix = Bits.GetBytes(PAYMENT_CHANNEL_CLAIM_PREFIX);
            byte[] channel = Hash256.FromHex((string)json["channel"]).Buffer;
            byte[] amount = Uint64.FromValue(int.Parse((string)json["amount"])).ToBytes();
            byte[] rv = new byte[prefix.Length + channel.Length + amount.Length];
            System.Buffer.BlockCopy(prefix, 0, rv, 0, prefix.Length);
            System.Buffer.BlockCopy(channel, 0, rv, prefix.Length, channel.Length);
            System.Buffer.BlockCopy(amount, 0, rv, prefix.Length + channel.Length, amount.Length);
            return rv.ToHex();
        }

        /// <summary>
        /// Encode a transaction into binary format in preparation for providing one signature towards a multi-signed transaction. (Only encodes fields that are intended to be signed.)
        /// </summary>
        /// <param name="json"></param>
        /// <param name="signingAccount"></param>
        /// <returns>string</returns>
        public static string EncodeForMulitSigning(object json, string signingAccount)
        {
            string accountID = new AccountId(signingAccount).ToHex();
            JToken token = JToken.FromObject(json);
            return SerializeJson(token, HashPrefix.TransactionMultiSig.Bytes(), accountID.FromHex(), true);
        }

        /// <summary>
        /// Encode a multi transaction - Batch
        /// </summary>
        /// <param name="flags">Batch flags.</param>
        /// <param name="txIDs">Collection of inner transaction IDs.</param>
        /// <param name="networkId">Optional network ID for cross‑chain replay protection.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static byte[] EncodeForSigningBatch(uint flags, IEnumerable<string> txIDs, uint? networkId = null)
        {
            if (txIDs == null) throw new ArgumentNullException(nameof(txIDs));

            var list = new BytesList();

            // 1) Префикс "BCH\0"
            list.Put(Bits.GetBytes((uint)HashPrefix.Batch));

            // 1.5) NetworkID (если есть)
            if (networkId.HasValue)
            {
                list.Put(new Uint32(networkId.Value).ToBytes());
            }

            // 2) Flags (UInt32 BE)
            list.Put(new Uint32(flags).ToBytes());

            // 3) Количество txIDs (UInt32 BE)
            list.Put(new Uint32((uint)txIDs.Count()).ToBytes());

            // 4) Каждый txid как 32 байта
            foreach (var id in txIDs)
            {
                var raw = Hash256.FromHex(id).Buffer; // validate hex string
                if (raw.Length != 32) throw new ArgumentException("txID must be 32 bytes (Hash256).");
                list.Put(raw);
            }

            return list.ToBytes();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="json"></param>
        /// <returns>string</returns>
        public static string SerializeJson(JToken json, byte[]? prefix = null, byte[]? suffix = null, bool signingOnly = false)
        {
            var list = new BytesList();
            if (prefix != null)
            {
                list.Put(prefix);
            }

            StObject so = StObject.FromJson(json, signingOnly);
            list.Put(so.ToBytes());

            if (suffix != null)
            {
                list.Put(suffix);
            }
            return list.BytesHex();
        }
    }
}