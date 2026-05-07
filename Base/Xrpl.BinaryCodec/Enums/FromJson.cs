using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Types;

namespace Xrpl.BinaryCodec.Enums
{
    public delegate ISerializedType FromJson(JsonNode token);
}
