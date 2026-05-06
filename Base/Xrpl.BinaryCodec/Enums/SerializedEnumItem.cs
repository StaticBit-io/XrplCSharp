using System;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Enums
{
    public abstract class SerializedEnumItem<TOrd> : EnumItem, ISerializedType
        where TOrd : struct, IConvertible
    {
        protected readonly byte[] Bytes; 
        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Bytes);
        }

        public JsonNode ToJson()
        {
            return JsonValue.Create(ToString());
        }

        protected SerializedEnumItem(string name, int ordinal) : base(name, ordinal)
        {
            var width = Marshal.SizeOf(default(TOrd));
            switch (width)
            {
                case 1:
                    Bytes = new[] { (byte)ordinal };
                    break;
                case 2:
                    Bytes = Bits.GetBytes((ushort) ordinal);
                    break;
            }
        }
    }
}