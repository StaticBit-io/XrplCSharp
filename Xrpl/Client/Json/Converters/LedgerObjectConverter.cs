using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Linq;

using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters;

public abstract class NodeConverterBase<TNode> : JsonConverter where TNode : NodeBase, new()
{
    public override bool CanConvert(Type objectType) =>
        typeof(TNode).IsAssignableFrom(objectType);

    protected static LedgerEntryType ReadLedgerEntryType(JObject jObject, JsonSerializer serializer)
    {
        var token = jObject["LedgerEntryType"];
        if (token == null)
            throw new JsonSerializationException("LedgerEntryType is missing.");

        return token.ToObject<LedgerEntryType>(serializer);
    }

    protected static TNode CreateBaseNode(JObject jObject, JsonSerializer serializer)
    {
        return new TNode
        {
            LedgerEntryType = ReadLedgerEntryType(jObject, serializer),
            LedgerIndex = jObject["LedgerIndex"]?.ToObject<string>(serializer) ?? string.Empty,
        };
    }

    protected static JObject CreateBaseJObject(NodeBase node, JsonSerializer serializer)
    {
        var jObject = new JObject
        {
            ["LedgerEntryType"] = JToken.FromObject(node.LedgerEntryType, serializer)
        };

        if (!string.IsNullOrWhiteSpace(node.LedgerIndex))
            jObject["LedgerIndex"] = node.LedgerIndex;

        return jObject;
    }
    protected static void RemoveNullProperties(JToken token)
    {
        if (token.Type == JTokenType.Object)
        {
            foreach (var prop in token.Children<JProperty>().ToList())
            {
                if (prop.Value.Type == JTokenType.Null)
                {
                    prop.Remove();
                }
                else
                {
                    RemoveNullProperties(prop.Value);
                }
            }
        }
        else if (token.Type == JTokenType.Array)
        {
            foreach (var child in token.Children().ToList())
            {
                RemoveNullProperties(child);
            }
        }
    }
    protected static void WriteLedgerEntry(JObject target, string propertyName, BaseLedgerEntry? entry, JsonSerializer serializer)
    {
        if (entry == null)
            return;

        var entryObject = JObject.FromObject(entry, serializer);
        entryObject.Remove(nameof(BaseLedgerEntry.LedgerEntryType));
        RemoveNullProperties(entryObject);

        if (entryObject.HasValues)
            target[propertyName] = entryObject;
    }

    public override bool CanWrite => true;

    public abstract override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer);
}

public class ModifiedNodeConverter : NodeConverterBase<ModifiedNode>
{
    public override object ReadJson(
        JsonReader reader,
        System.Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);

        var node = CreateBaseNode(jObject, serializer);

        node.PreviousTxnID = jObject["PreviousTxnID"]?.ToObject<string>(serializer);
        node.PreviousTxnLgrSeq = jObject["PreviousTxnLgrSeq"]?.ToObject<uint?>(serializer);

        node.FinalFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            jObject["FinalFields"],
            serializer);

        node.PreviousFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            jObject["PreviousFields"],
            serializer);

        return node;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        if (value is not ModifiedNode node)
            throw new JsonSerializationException($"Expected {nameof(ModifiedNode)}, got {value.GetType().Name}.");

        var jObject = CreateBaseJObject(node, serializer);

        WriteLedgerEntry(jObject, "FinalFields", node.FinalFields, serializer);
        WriteLedgerEntry(jObject, "PreviousFields", node.PreviousFields, serializer);

        if (!string.IsNullOrWhiteSpace(node.PreviousTxnID))
            jObject["PreviousTxnID"] = node.PreviousTxnID;

        if (node.PreviousTxnLgrSeq.HasValue)
            jObject["PreviousTxnLgrSeq"] = node.PreviousTxnLgrSeq.Value;

        jObject.WriteTo(writer);
    }
}

public class CreatedNodeConverter : NodeConverterBase<CreatedNode>
{
    public override object ReadJson(
        JsonReader reader,
        System.Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);

        var node = CreateBaseNode(jObject, serializer);

        node.NewFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            jObject["NewFields"],
            serializer);

        return node;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        if (value is not CreatedNode node)
            throw new JsonSerializationException($"Expected {nameof(CreatedNode)}, got {value.GetType().Name}.");

        var jObject = CreateBaseJObject(node, serializer);

        WriteLedgerEntry(jObject, "NewFields", node.NewFields, serializer);

        jObject.WriteTo(writer);
    }
}

public class DeletedNodeConverter : NodeConverterBase<DeletedNode>
{
    public override object ReadJson(
        JsonReader reader,
        System.Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);

        var node = CreateBaseNode(jObject, serializer);

        node.FinalFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            jObject["FinalFields"],
            serializer);

        node.PreviousFields = LOConverter.GetBaseRippleLO(
            node.LedgerEntryType,
            jObject["PreviousFields"],
            serializer);

        return node;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        if (value is not DeletedNode node)
            throw new JsonSerializationException($"Expected {nameof(DeletedNode)}, got {value.GetType().Name}.");

        var jObject = CreateBaseJObject(node, serializer);

        WriteLedgerEntry(jObject, "FinalFields", node.FinalFields, serializer);
        WriteLedgerEntry(jObject, "PreviousFields", node.PreviousFields, serializer);

        jObject.WriteTo(writer);
    }
}

/// <summary>
/// <see cref="BaseLedgerEntry"/> json converter
/// </summary>
public class LOConverter : JsonConverter
{
    /// <summary>
    /// Convert ledger entry json object to standard type
    /// </summary>
    /// <param name="type">field type</param>
    /// <param name="token">current json object</param>
    /// <param name="serializer"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static BaseLedgerEntry? GetBaseRippleLO(
        LedgerEntryType type,
        JToken? token,
        JsonSerializer serializer)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return null;
        }

        var result = type switch
        {
            LedgerEntryType.AccountRoot => token.ToObject<LOAccountRoot>(serializer),
            LedgerEntryType.Amendments => token.ToObject<LOAmendments>(serializer),
            LedgerEntryType.DirectoryNode => token.ToObject<LODirectoryNode>(serializer),
            LedgerEntryType.Escrow => token.ToObject<LOEscrow>(serializer),
            LedgerEntryType.FeeSettings => token.ToObject<LOFeeSettings>(serializer),
            LedgerEntryType.LedgerHashes => token.ToObject<LOLedgerHashes>(serializer),
            LedgerEntryType.Offer => token.ToObject<LOOffer>(serializer),
            LedgerEntryType.PayChannel => token.ToObject<LOPayChannel>(serializer),
            LedgerEntryType.RippleState => token.ToObject<LORippleState>(serializer),
            LedgerEntryType.SignerList => token.ToObject<LOSignerList>(serializer),
            LedgerEntryType.NFTokenOffer => token.ToObject<LONFTokenOffer>(serializer),
            LedgerEntryType.NegativeUNL => token.ToObject<LONegativeUNL>(serializer),
            LedgerEntryType.NFTokenPage => token.ToObject<LONFTokenPage>(serializer),
            LedgerEntryType.Ticket => token.ToObject<LOTicket>(serializer),
            LedgerEntryType.AMM => token.ToObject<LOAmm>(serializer),
            LedgerEntryType.Check => token.ToObject<LOCheck>(serializer),
            LedgerEntryType.MPToken => token.ToObject<LOMPToken>(serializer),
            LedgerEntryType.MPTokenIssuance => token.ToObject<LOMPTokenIssuance>(serializer),
            LedgerEntryType.Oracle => token.ToObject<LOOracle>(serializer),
            LedgerEntryType.DID => token.ToObject<LODID>(serializer),
            LedgerEntryType.PermissionedDomain => token.ToObject<LOPermissionedDomain>(serializer),
            LedgerEntryType.Credential => token.ToObject<LOCredential>(serializer),
            LedgerEntryType.DepositPreauth => token.ToObject<LODepositPreauth>(serializer),
            _ => token.ToObject<BaseLedgerEntry>(serializer),
        };

        if (result != null)
        {
            result.LedgerEntryType = type;

            if (string.IsNullOrWhiteSpace(result.LedgerIndex))
            {
                result.LedgerIndex = token["LedgerIndex"]?.ToObject<string>(serializer);
            }

            if (string.IsNullOrWhiteSpace(result.Index))
            {
                result.Index = token["index"]?.ToObject<string>(serializer);
            }
        }

        return result;
    }

    /// <summary>
    /// Writes a <see cref="BaseLedgerEntry"/> to JSON.
    /// Null fields are ignored based on the serializer settings.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The <see cref="BaseLedgerEntry"/> value to serialize.</param>
    /// <param name="serializer">The JSON serializer.</param>
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var jObject = JObject.FromObject(value, serializer);
        jObject.WriteTo(writer);
    }

    /// <summary>
    /// create <see cref="BaseLedgerEntry"/> 
    /// </summary>
    /// <param name="objectType"></param>
    /// <param name="jObject">json object LedgerEntity</param>
    /// <returns></returns>
    public BaseLedgerEntry Create(Type objectType, JObject jObject)
    {
        switch (objectType.Name)
        {
            case "LOAccountRoot":
                return new LOAccountRoot();

            case "LOAmendments":
                return new LOAmendments();

            case "LODirectoryNode":
                return new LODirectoryNode();

            case "LOEscrow":
                return new LOEscrow();

            case "LOFeeSettings":
                return new LOFeeSettings();

            case "LOLedgerHashes":
                return new LOLedgerHashes();

            case "LOOffer":
                return new LOOffer();

            case "LOPayChannel":
                return new LOPayChannel();

            case "LORippleState":
                return new LORippleState();

            case "LOSignerList":
                return new LOSignerList();

            case "LONFTokenOffer":
                return new LONFTokenOffer();

            case "LONFTokenPage":
                return new LONFTokenPage();

            case "LOTicket":
                return new LOTicket();

            case "LONegativeUNL":
                return new LONegativeUNL();

            case "LOAmm":
                return new LOAmm();

            case "LOCheck":
                return new LOCheck();

            case "LOMPToken":
                return new LOMPToken();

            case "LOMPTokenIssuance":
                return new LOMPTokenIssuance();

            case "LOOracle":
                return new LOOracle();

            case "LODID":
                return new LODID();

            case "LOPermissionedDomain":
                return new LOPermissionedDomain();

            case "LOCredential":
                return new LOCredential();
        }

        var ledgerEntryType = jObject.Property("LedgerEntryType")?.Value.ToString();
        return ledgerEntryType switch
        {
            "AccountRoot" => new LOAccountRoot(),
            "Amendments" => new LOAmendments(),
            "DirectoryNode" => new LODirectoryNode(),
            "Escrow" => new LOEscrow(),
            "FeeSettings" => new LOFeeSettings(),
            "LedgerHashes" => new LOLedgerHashes(),
            "Offer" => new LOOffer(),
            "PayChannel" => new LOPayChannel(),
            "RippleState" => new LORippleState(),
            "SignerList" => new LOSignerList(),
            "NegativeUNL" => new LONegativeUNL(),
            "NFTokenOffer" => new LONFTokenOffer(),
            "NFTokenPage" => new LONFTokenPage(),
            "Ticket" => new LOTicket(),
            "Check" => new LOCheck(),
            "DepositPreauth" => new LODepositPreauth(),
            "Amm" => new LOAmm(),
            "MPToken" => new LOMPToken(),
            "MPTokenIssuance" => new LOMPTokenIssuance(),
            "Oracle" => new LOOracle(),
            "DID" => new LODID(),
            "PermissionedDomain" => new LOPermissionedDomain(),
            "Credential" => new LOCredential(),
            _ => new BaseLedgerEntry(), // throw new Exception("Can't create ledger type" + ledgerEntryType)
        };
    }

    /// <summary> read <see cref="BaseLedgerEntry"/>  from json object </summary>
    /// <param name="reader">json reader</param>
    /// <param name="objectType">object type</param>
    /// <param name="existingValue">object value</param>
    /// <param name="serializer">json serializer</param>
    /// <returns><see cref="BaseLedgerEntry"/> </returns>
    /// <exception cref="NotSupportedException">Cannot convert value</exception>
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);
        var target = Create(objectType, jObject);
        serializer.Populate(reader: jObject.CreateReader(), target);
        return target;
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType) => typeof(BaseLedgerEntry).IsAssignableFrom(objectType);

    public override bool CanWrite => false;
}