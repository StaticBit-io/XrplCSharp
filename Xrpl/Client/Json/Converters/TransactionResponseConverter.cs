using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;

using Xrpl.Models.Transactions;

//https://xrpl.org/transaction-types.html

namespace Xrpl.Client.Json.Converters
{
    /// <summary> Transaction json Converter </summary>
    public class TransactionResponseConverter : JsonConverter
    {
        /// <summary>
        /// write <see cref="ITransactionResponse"/>  to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"><see cref="ITransactionResponse"/> value</param>
        /// <param name="serializer">json serializer</param>
        /// <exception cref="NotSupportedException">Cannot write this object type</exception>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// create <see cref="ITransactionResponse"/> 
        /// </summary>
        /// <param name="jObject">json object LedgerEntity</param>
        /// <returns><see cref="ITransactionResponse"/> </returns>
        public ITransactionResponse Create(JObject jObject)
        {
            return jObject.Property("TransactionType")?.Value.ToString() switch
            {
                "AccountSet" => new AccountSetResponse(),
                "AccountDelete" => new AccountDeleteResponse(),

                "CheckCancel" => new CheckCancelResponse(),
                "CheckCash" => new CheckCashResponse(),
                "CheckCreate" => new CheckCreateResponse(),

                "DepositPreauth" => new DepositPreauthResponse(),

                "EscrowCancel" => new EscrowCancelResponse(),
                "EscrowCreate" => new EscrowCreateResponse(),
                "EscrowFinish" => new EscrowFinishResponse(),

                "NFTokenAcceptOffer" => new NFTokenAcceptOfferResponse(),
                "NFTokenCancelOffer" => new NFTokenCancelOfferResponse(),
                "NFTokenBurn" => new NFTokenBurnResponse(),
                "NFTokenModify" => new NFTokenModifyResponse(),
                "NFTokenCreateOffer" => new NFTokenCreateOfferResponse(),
                "NFTokenMint" => new NFTokenMintResponse(),

                "OfferCancel" => new OfferCancelResponse(),
                "OfferCreate" => new OfferCreateResponse(),

                "Payment" => new PaymentResponse(),
                "PaymentChannelClaim" => new PaymentChannelClaimResponse(),
                "PaymentChannelCreate" => new PaymentChannelCreateResponse(),
                "PaymentChannelFund" => new PaymentChannelFundResponse(),

                "SetRegularKey" => new SetRegularKeyResponse(),
                "SignerListSet" => new SignerListSetResponse(),
                "TicketCreate" => new TicketCreateResponse(),
                "TrustSet" => new TrustSetResponse(),
                "EnableAmendment" => new EnableAmendmentResponse(),
                "SetFee" => new SetFeeResponse(),
                "UNLModify" => new UNLModifyResponse(),

                "AMMBid" => new AMMBidResponse(),
                "AMMCreate" => new AMMCreateResponse(),
                "AMMDelete" => new AMMDeleteResponse(),
                "AMMDeposit" => new AMMDepositResponse(),
                "AMMVote" => new AMMVoteResponse(),
                "AMMWithdraw" => new AMMWithdrawResponse(),

                "Clawback" => new ClawBackResponse(),

                "Batch" => new BatchResponse(),

                "MPTokenAuthorize" => new MPTokenAuthorizeResponse(),
                "MPTokenIssuanceCreate" => new MPTokenIssuanceCreateResponse(),
                "MPTokenIssuanceDestroy" => new MPTokenIssuanceDestroyResponse(),
                "MPTokenIssuanceSet" => new MPTokenIssuanceSetResponse(),
                //_ => throw new Exception("Can't create transaction type" + transactionType)
                _ => SetUnknownType(jObject),
            };
        }

        static TransactionResponse SetUnknownType(JObject jObject)
        {
            jObject.Property("TransactionType").Value = "Unknown";
            return new TransactionResponse();
        }

        /// <summary> read  <see cref="ITransactionResponse"/>   from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="objectType">object type</param>
        /// <param name="existingValue">object value</param>
        /// <param name="serializer">json serializer</param>
        /// <returns><see cref="ITransactionResponse"/> </returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jObject = JObject.Load(reader);
            
            ITransactionResponse transaction = Create(jObject);
            serializer.Populate(jObject.CreateReader(), transaction);
            return transaction;
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType) => typeof(ITransactionResponse).IsAssignableFrom(objectType);

        /// <inheritdoc />
        public override bool CanWrite => false;
    }
}