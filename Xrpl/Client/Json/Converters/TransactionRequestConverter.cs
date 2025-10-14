using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters;
/// <summary> Transaction json Converter </summary>
public class TransactionRequestConverter : JsonConverter
{
    /// <summary>
    /// write <see cref="ITransactionResponseCommon"/>  to json object
    /// </summary>
    /// <param name="writer">writer</param>
    /// <param name="value"><see cref="ITransactionResponseCommon"/> value</param>
    /// <param name="serializer">json serializer</param>
    /// <exception cref="NotSupportedException">Cannot write this object type</exception>
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// create <see cref="ITransactionCommon"/> 
    /// </summary>
    /// <param name="jObject">json object LedgerEntity</param>
    /// <returns><see cref="ITransactionCommon"/> </returns>
    public ITransactionCommon Create(JObject jObject)
    {
        return jObject.Property("TransactionType")?.Value.ToString() switch
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
            "Batch" => new Batch(),
            //_ => throw new Exception("Can't create transaction type" + transactionType)
            _ => SetUnknownType(jObject),
        };
    }

    static TransactionCommon SetUnknownType(JObject jObject)
    {
        jObject.Property("TransactionType").Value = "Unknown";
        return new TransactionUnknown();
    }

    private class TransactionUnknown : TransactionCommon, ITransactionCommon
    {
    }
    /// <summary> read  <see cref="ITransactionCommon"/>   from json object </summary>
    /// <param name="reader">json reader</param>
    /// <param name="objectType">object type</param>
    /// <param name="existingValue">object value</param>
    /// <param name="serializer">json serializer</param>
    /// <returns><see cref="ITransactionCommon"/> </returns>
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JObject jObject = JObject.Load(reader);

        ITransactionCommon transactionCommon = Create(jObject);
        serializer.Populate(jObject.CreateReader(), transactionCommon);
        return transactionCommon;
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType) => objectType == typeof(ITransactionCommon);

    /// <inheritdoc />
    public override bool CanWrite => false;
}