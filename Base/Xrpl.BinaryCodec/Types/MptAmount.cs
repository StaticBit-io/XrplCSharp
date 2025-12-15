using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Types
{
    public class MptAmount : Amount
    {
        public readonly Hash192 MptIssuanceId;
        public readonly bool IsPositive;
        public readonly ulong MptValue;

        public MptAmount(string value, string mptIssuanceId) 
            : base(AmountValue.FromMpt(value), null, null)
        {
            var parsed = decimal.Parse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            IsPositive = parsed >= 0;
            MptValue = (ulong)Math.Abs(parsed);
            MptIssuanceId = Hash192.FromHex(mptIssuanceId);
        }

        public MptAmount(bool isPositive, ulong mptValue, Hash192 mptIssuanceId)
            : base(AmountValue.FromMpt(mptValue.ToString()), null, null)
        {
            IsPositive = isPositive;
            MptValue = mptValue;
            MptIssuanceId = mptIssuanceId;
        }

        public override void ToBytes(IBytesSink sink)
        {
            byte leadingByte = (byte)(IsPositive ? 0x60 : 0x20);
            sink.Put(new[] { leadingByte });
            
            var amountBytes = Bits.GetBytes(MptValue);
            sink.Put(amountBytes);
            
            MptIssuanceId.ToBytes(sink);
        }

        public override JToken ToJson()
        {
            var sign = IsPositive ? "" : "-";
            return new JObject
            {
                ["mpt_issuance_id"] = MptIssuanceId.ToString(),
                ["value"] = $"{sign}{MptValue}",
            };
        }

        public static new MptAmount FromParser(BinaryParser parser, int? hint = null)
        {
            var bytes = parser.Read(33);
            return FromBytes(bytes);
        }

        public static MptAmount FromBytes(byte[] bytes)
        {
            if (bytes.Length != 33)
                throw new ArgumentException("MPT Amount must be exactly 33 bytes", nameof(bytes));

            var leadingByte = bytes[0];
            var isPositive = (leadingByte & 0x40) != 0;
            
            var amountBytes = new byte[8];
            Array.Copy(bytes, 1, amountBytes, 0, 8);
            var mptValue = Bits.ToUInt64(amountBytes, 0);
            
            var hash192Bytes = new byte[24];
            Array.Copy(bytes, 9, hash192Bytes, 0, 24);
            var mptIssuanceId = new Hash192(hash192Bytes);
            
            return new MptAmount(isPositive, mptValue, mptIssuanceId);
        }
    }
}
