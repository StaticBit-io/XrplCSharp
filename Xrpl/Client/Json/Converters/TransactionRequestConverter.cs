using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters;
/// <summary> Transaction json Converter </summary>
public class TransactionRequestConverter : JsonConverter<ITransactionRequest>
{
    /// <summary>
    /// Writes an <see cref="ITransactionRequest"/> to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, ITransactionRequest value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // Remove this converter to avoid infinite recursion
        JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
        innerOptions.Converters.Remove(this);

        JsonSerializer.Serialize(writer, value, value.GetType(), innerOptions);
    }

    /// <summary>
    /// create <see cref="ITransactionRequest"/> by TransactionType discriminator
    /// </summary>
    public static ITransactionRequest Create(string transactionType)
    {
        return transactionType switch
        {
            "AccountSet" => new AccountSet(),
            "AccountDelete" => new AccountDelete(),

            "CheckCancel" => new CheckCancel(),
            "CheckCash" => new CheckCash(),
            "CheckCreate" => new CheckCreate(),

            "DepositPreauth" => new DepositPreauth(),

            "EscrowCancel" => new EscrowCancel(),
            "EscrowCreate" => new EscrowCreate(),
            "EscrowFinish" => new EscrowFinish(),

            "NFTokenAcceptOffer" => new NFTokenAcceptOffer(),
            "NFTokenCancelOffer" => new NFTokenCancelOffer(),
            "NFTokenBurn" => new NFTokenBurn(),
            "NFTokenCreateOffer" => new NFTokenCreateOffer(),
            "NFTokenMint" => new NFTokenMint(),
            "NFTokenModify" => new NFTokenModify(),

            "OfferCancel" => new OfferCancel(),
            "OfferCreate" => new OfferCreate(),

            "Payment" => new Payment(),
            "PaymentChannelClaim" => new PaymentChannelClaim(),
            "PaymentChannelCreate" => new PaymentChannelCreate(),
            "PaymentChannelFund" => new PaymentChannelFund(),

            "SetRegularKey" => new SetRegularKey(),
            "SignerListSet" => new SignerListSet(),
            "TicketCreate" => new TicketCreate(),
            "TrustSet" => new TrustSet(),
            "EnableAmendment" => new EnableAmendment(),
            "SetFee" => new SetFee(),
            "UNLModify" => new UNLModify(),

            "AMMBid" => new AMMBid(),
            "AMMCreate" => new AMMCreate(),
            "AMMDelete" => new AMMDelete(),
            "AMMDeposit" => new AMMDeposit(),
            "AMMVote" => new AMMVote(),
            "AMMWithdraw" => new AMMWithdraw(),

            "Clawback" => new ClawBack(),
            "AMMClawback" => new AMMClawBack(),

            "Batch" => new Batch(),

            "MPTokenAuthorize" => new MPTokenAuthorize(),
            "MPTokenIssuanceCreate" => new MPTokenIssuanceCreate(),
            "MPTokenIssuanceDestroy" => new MPTokenIssuanceDestroy(),
            "MPTokenIssuanceSet" => new MPTokenIssuanceSet(),

            "OracleSet" => new OracleSet(),
            "OracleDelete" => new OracleDelete(),
            "DIDSet" => new DIDSet(),
            "DIDDelete" => new DIDDelete(),
            "PermissionedDomainSet" => new PermissionedDomainSet(),
            "PermissionedDomainDelete" => new PermissionedDomainDelete(),
            "CredentialCreate" => new CredentialCreate(),
            "CredentialAccept" => new CredentialAccept(),
            "CredentialDelete" => new CredentialDelete(),
            //_ => throw new Exception("Can't create transaction type" + transactionType)
            _ => new TransactionUnknown(),
        };
    }

    private class TransactionUnknown : TransactionRequest, ITransactionRequest
    {
    }

    /// <summary> read  <see cref="ITransactionRequest"/>   from json object </summary>
    public override ITransactionRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        string transactionType = root.TryGetProperty("TransactionType", out JsonElement ttEl)
            ? ttEl.GetString()
            : null;

        ITransactionRequest transactionRequest = Create(transactionType);
        string rawJson = root.GetRawText();

        // Remove this converter to avoid infinite recursion
        JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
        for (int i = innerOptions.Converters.Count - 1; i >= 0; i--)
        {
            if (innerOptions.Converters[i] is TransactionRequestConverter)
                innerOptions.Converters.RemoveAt(i);
        }

        try
        {
            return (ITransactionRequest)JsonSerializer.Deserialize(rawJson, transactionRequest.GetType(), innerOptions);
        }
        catch (JsonException)
        {
            return transactionRequest;
        }
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeof(ITransactionRequest).IsAssignableFrom(typeToConvert);
}
