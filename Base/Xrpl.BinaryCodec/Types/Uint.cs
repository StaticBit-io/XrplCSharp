using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;

//https://github.com/XRPLF/xrpl.js/blob/8a9a9bcc28ace65cde46eed5010eb8927374a736/packages/ripple-binary-codec/src/types/uint.ts

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// Base class for serializing and deserializing unsigned integers.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class Uint<T> : ISerializedType where T: struct, IConvertible
    {
        /// <summary>
        /// integers value
        /// </summary>
        public readonly T Value;
        /// <summary>
        /// create instance of this integer value
        /// </summary>
        /// <param name="value"></param>
        protected Uint(T value)
        {
            Value = value;
        }

        /// <inheritdoc />
        public void ToBytes(IBytesSink sink) => sink.Put(ToBytes());

        /// <summary>
        /// Reads an unsigned integer from a JSON number or a base-10 string. Works for a node parsed
        /// from text and for one built in memory from any CLR numeric type, such as the
        /// <c>uint</c> that <see cref="ToJson"/> emits.
        /// </summary>
        /// <param name="token">json token</param>
        /// <param name="maxValue">largest value the type can hold</param>
        /// <exception cref="OverflowException">the value is negative or above <paramref name="maxValue"/></exception>
        private protected static ulong ParseJsonUnsigned(JsonNode token, ulong maxValue)
        {
            string text = token.GetValueKind() == System.Text.Json.JsonValueKind.Number
                ? token.ToJsonString()
                : token.GetValue<string>();
            if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) || value > maxValue)
            {
                throw new OverflowException($"Value {text} is not an integer in the range of {typeof(T).Name} (0..{maxValue}).");
            }
            return value;
        }

        /// <inheritdoc />
        public virtual JsonNode ToJson() => JsonValue.Create(Convert.ToUInt32(Value));

        /// <inheritdoc />
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// convert to bytes array
        /// </summary>
        public abstract byte[] ToBytes();
    }
}