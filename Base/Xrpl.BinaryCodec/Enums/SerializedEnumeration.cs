using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;

namespace Xrpl.BinaryCodec.Enums
{
    public abstract class SerializedEnumeration<TEnum, TOrd> : Enumeration<TEnum> 
        where TEnum : SerializedEnumItem<TOrd>
        where TOrd : struct, IConvertible
    {
        protected SerializedEnumeration()
        {
            Width = Marshal.SizeOf(default(TOrd));
        }

        public int Width { get;}

        public TEnum FromParser(BinaryParser parser, int? hint = null)
        {
            return this[ReadOrdinal(parser)];
        }

        public TEnum FromJson(JsonNode value)
        {
            return value.GetValueKind() == JsonValueKind.String ? 
                this[value.GetValue<string>()] : this[value.GetValue<int>()];
        }

        public int ReadOrdinal(BinaryParser parser)
        {
            return parser.Read(Width).Aggregate(0, (a, b) => (a >> 8) + b);
        }
    }
}