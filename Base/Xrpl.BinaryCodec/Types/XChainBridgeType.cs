using System;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// XChainBridge composite type (type code 25).
    /// Serialized as: LockingChainDoor (AccountID) + LockingChainIssue (Issue)
    ///              + IssuingChainDoor (AccountID) + IssuingChainIssue (Issue)
    /// </summary>
    public class XChainBridgeType : ISerializedType
    {
        public readonly AccountId LockingChainDoor;
        public readonly Issue LockingChainIssue;
        public readonly AccountId IssuingChainDoor;
        public readonly Issue IssuingChainIssue;

        public XChainBridgeType(AccountId lockingDoor, Issue lockingIssue,
                                AccountId issuingDoor, Issue issuingIssue)
        {
            LockingChainDoor = lockingDoor;
            LockingChainIssue = lockingIssue;
            IssuingChainDoor = issuingDoor;
            IssuingChainIssue = issuingIssue;
        }

        public void ToBytes(IBytesSink sink)
        {
            // AccountID fields inside XChainBridge are VL-encoded (length-prefixed)
            // just like STAccount in rippled's STXChainBridge::add()
            sink.Put(BinarySerializer.EncodeVl(20));
            LockingChainDoor.ToBytes(sink);
            LockingChainIssue.ToBytes(sink);
            sink.Put(BinarySerializer.EncodeVl(20));
            IssuingChainDoor.ToBytes(sink);
            IssuingChainIssue.ToBytes(sink);
        }

        public JsonNode ToJson()
        {
            return new JsonObject
            {
                ["LockingChainDoor"] = (JsonNode)LockingChainDoor,
                ["LockingChainIssue"] = LockingChainIssue.ToJson(),
                ["IssuingChainDoor"] = (JsonNode)IssuingChainDoor,
                ["IssuingChainIssue"] = IssuingChainIssue.ToJson()
            };
        }

        public static XChainBridgeType FromJson(JsonNode token)
        {
            if (token is not JsonObject obj)
                throw new InvalidJsonException("XChainBridge must be a JSON object.");

            // Exactly these four, by name. A fifth member used to be read past in silence and left
            // out of whatever blob or id this bridge ended up in.
            //
            // Counting alone would not do it: three of the four plus one unknown member also comes
            // to four, and the check would pass while the missing one reached AccountId.FromJson or
            // Issue.FromJson as null - reporting whatever those make of nothing rather than naming
            // the member that does not belong.
            if (obj.Count != 4
                || !obj.ContainsKey("LockingChainDoor")
                || !obj.ContainsKey("LockingChainIssue")
                || !obj.ContainsKey("IssuingChainDoor")
                || !obj.ContainsKey("IssuingChainIssue"))
            {
                throw new InvalidJsonException(
                    "XChainBridge object must contain exactly 'LockingChainDoor', 'LockingChainIssue', " +
                    "'IssuingChainDoor' and 'IssuingChainIssue'.");
            }

            AccountId lockingDoor = AccountId.FromJson(obj["LockingChainDoor"]);
            Issue lockingIssue = Issue.FromJson(obj["LockingChainIssue"]);
            AccountId issuingDoor = AccountId.FromJson(obj["IssuingChainDoor"]);
            Issue issuingIssue = Issue.FromJson(obj["IssuingChainIssue"]);

            return new XChainBridgeType(lockingDoor, lockingIssue, issuingDoor, issuingIssue);
        }

        public static XChainBridgeType FromParser(BinaryParser parser, int? hint = null)
        {
            int lockingDoorLen = parser.ReadVlLength();
            if (lockingDoorLen != 20)
                throw new FormatException($"Invalid LockingChainDoor length: {lockingDoorLen}. Expected 20.");
            AccountId lockingDoor = new AccountId(parser.Read(lockingDoorLen));
            Issue lockingIssue = Issue.FromParser(parser);
            int issuingDoorLen = parser.ReadVlLength();
            if (issuingDoorLen != 20)
                throw new FormatException($"Invalid IssuingChainDoor length: {issuingDoorLen}. Expected 20.");
            AccountId issuingDoor = new AccountId(parser.Read(issuingDoorLen));
            Issue issuingIssue = Issue.FromParser(parser);

            return new XChainBridgeType(lockingDoor, lockingIssue, issuingDoor, issuingIssue);
        }
    }
}
