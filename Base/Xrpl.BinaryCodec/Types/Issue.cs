using System;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-binary-codec/src/types/issue.ts

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Represents an Issue (currency + optional issuer, or MPT issuance ID).
    /// Supports XRP, IOU (currency+issuer), and MPT (mpt_issuance_id) formats.
    /// Binary format:
    ///   XRP:  20 bytes (currency, all zeros)
    ///   IOU:  40 bytes (20 currency + 20 issuer)
    ///   MPT:  44 bytes (20 account + 20 noAccount marker + 4 sequence)
    /// MPT binary layout matches rippled's STIssue serialization:
    ///   - 20 bytes: AccountID extracted from MPTokenIssuanceID
    ///   - 20 bytes: noAccount() = uint160{1} (19 zeros + 0x01)
    ///   - 4 bytes: token sequence (raw bytes from MPTID, effectively little-endian
    ///     due to rippled's memcpy+add32 pattern on LE platforms)
    /// </summary>
    public class Issue : ISerializedType
    {
        /// <summary>
        /// 20-byte noAccount marker: uint160(1) — 19 zero bytes followed by 0x01.
        /// Used in the "issuer" position to indicate MPT issue in binary format.
        /// </summary>
        private static readonly byte[] NoAccountBytes =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01
        };

        public readonly Currency Currency;
        public readonly AccountId Issuer;
        public readonly Hash192 MptIssuanceId;

        /// <summary>
        /// XRP Issue: only currency, no issuer.
        /// </summary>
        public Issue()
        {
            Currency = Currency.Xrp;
            Issuer = null;
            MptIssuanceId = null;
        }

        /// <summary>
        /// IOU Issue: currency with specified issuer.
        /// </summary>
        public Issue(Currency currency, AccountId issuer)
        {
            Currency = currency ?? Currency.Xrp;
            Issuer = issuer;
            MptIssuanceId = null;
        }

        /// <summary>
        /// MPT Issue: identified by MPTokenIssuanceID.
        /// </summary>
        public Issue(Hash192 mptIssuanceId)
        {
            Currency = null;
            Issuer = null;
            MptIssuanceId = mptIssuanceId ?? throw new ArgumentNullException(nameof(mptIssuanceId));
        }

        /// <summary>
        /// True if this issue represents an MPT.
        /// </summary>
        public bool IsMpt => MptIssuanceId != null;

        /// <inheritdoc/>
        public void ToBytes(IBytesSink sink)
        {
            if (IsMpt)
            {
                // MPT binary: account(20) + noAccount(20) + sequence(4)
                // MPTID = sequence(4 bytes BE) + account(20 bytes) = 24 bytes total
                byte[] mptIdBytes = MptIssuanceId.Buffer;

                // Extract account (bytes 4..23 of MPTID)
                byte[] account = new byte[20];
                Buffer.BlockCopy(mptIdBytes, 4, account, 0, 20);

                // Extract sequence bytes (bytes 0..3 of MPTID) and reverse byte order.
                // MPTID stores sequence as big-endian (00 00 00 01 for seq=1).
                // rippled's memcpy+add32 pattern on LE platforms effectively reverses
                // the byte order, producing 01 00 00 00 in the binary stream.
                byte[] seqBytes = new byte[4];
                seqBytes[0] = mptIdBytes[3];
                seqBytes[1] = mptIdBytes[2];
                seqBytes[2] = mptIdBytes[1];
                seqBytes[3] = mptIdBytes[0];

                sink.Put(account);        // 20 bytes: AccountID from MPTID
                sink.Put(NoAccountBytes);  // 20 bytes: noAccount() = uint160{1}
                sink.Put(seqBytes);        // 4 bytes: sequence (raw bytes from MPTID)
            }
            else
            {
                // Standard: 20 bytes currency
                Currency.ToBytes(sink);
                // For IOU (non-XRP), write 20 bytes issuer
                if (!Currency.IsNative)
                {
                    Issuer.ToBytes(sink);
                }
            }
        }

        /// <inheritdoc/>
        public JsonNode ToJson()
        {
            if (IsMpt)
            {
                return new JsonObject
                {
                    ["mpt_issuance_id"] = MptIssuanceId.ToString()
                };
            }

            if (Currency.IsNative)
            {
                return new JsonObject
                {
                    ["currency"] = Currency.ToString()
                };
            }

            return new JsonObject
            {
                ["currency"] = (JsonNode)Currency,
                ["issuer"] = (JsonNode)Issuer
            };
        }

        /// <summary>
        /// Deserialize from JSON, distinguishing XRP, IOU, and MPT issues.
        /// </summary>
        public static Issue FromJson(JsonNode token)
        {
            if (!(token is JsonObject))
                throw new InvalidJsonException($"Issue must be a JSON object, got {token.GetValueKind()}");

            JsonObject obj = token.AsObject();

            // MPT format: { "mpt_issuance_id": "..." }
            if (obj.ContainsKey("mpt_issuance_id"))
            {
                string mptId = obj["mpt_issuance_id"]?.GetValue<string>();
                if (mptId is null)
                    throw new InvalidJsonException("Issue mpt_issuance_id must be a string.");
                return new Issue(Hash192.FromHex(mptId));
            }

            // Standard format: { "currency": "...", "issuer": "..." }
            string currencyStr = obj["currency"]?.GetValue<string>();
            if (currencyStr is null)
                throw new InvalidJsonException("Issue object must contain property 'currency' or 'mpt_issuance_id'.");

            Currency currency = Currency.FromString(currencyStr);

            if (currency.IsNative)
            {
                if (obj.Count != 1)
                    throw new InvalidJsonException("XRP Issue object must contain only 'currency'.");
                return new Issue();
            }

            if (obj.Count != 2)
                throw new InvalidJsonException("Issued currency object must contain exactly 'currency' and 'issuer'.");

            string issuerStr = obj["issuer"]?.GetValue<string>();
            if (issuerStr is null)
                throw new InvalidJsonException("Issue object must contain property 'issuer'.");

            AccountId issuer = new AccountId(issuerStr);
            return new Issue(currency, issuer);
        }

        /// <summary>
        /// Read from binary parser: supports XRP (20 bytes), IOU (40 bytes), and MPT (44 bytes).
        /// Reads 20 bytes as "currency" (or account for MPT), then 20 bytes as "issuer".
        /// If issuer == noAccount (uint160{1}), it's MPT: reads 4 more bytes as sequence.
        /// </summary>
        public static Issue FromParser(BinaryParser parser, int? hint = null)
        {
            // Read first 20 bytes (currency for XRP/IOU, or account for MPT)
            byte[] firstBytes = parser.Read(20);

            Currency curr = new Currency(firstBytes);
            if (curr.IsNative)
            {
                // XRP: just 20 zero bytes
                return new Issue(curr, null);
            }

            // Non-XRP: read 20 more bytes (issuer for IOU, or noAccount marker for MPT)
            byte[] secondBytes = parser.Read(20);

            if (IsNoAccount(secondBytes))
            {
                // MPT: the first 20 bytes are the account, second 20 are the noAccount marker
                // Read 4 more bytes as the token sequence
                byte[] seqBytes = parser.Read(4);

                // Reconstruct MPTID = sequence(4 bytes BE) + account(20 bytes) = 24 bytes
                // Reverse sequence bytes back to big-endian (stream has them reversed)
                byte[] mptIdBytes = new byte[24];
                mptIdBytes[0] = seqBytes[3];
                mptIdBytes[1] = seqBytes[2];
                mptIdBytes[2] = seqBytes[1];
                mptIdBytes[3] = seqBytes[0];
                System.Buffer.BlockCopy(firstBytes, 0, mptIdBytes, 4, 20);

                return new Issue(new Hash192(mptIdBytes));
            }

            // IOU: first 20 bytes = currency, second 20 bytes = issuer
            AccountId issuer = new AccountId(secondBytes);
            return new Issue(curr, issuer);
        }

        /// <summary>
        /// Check if bytes match the noAccount marker: uint160{1} (19 zeros + 0x01).
        /// </summary>
        private static bool IsNoAccount(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 20)
                return false;
            for (int i = 0; i < 19; i++)
            {
                if (bytes[i] != 0)
                    return false;
            }
            return bytes[19] == 0x01;
        }
    }
}
