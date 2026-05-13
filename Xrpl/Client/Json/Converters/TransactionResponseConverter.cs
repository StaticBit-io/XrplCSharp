using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models.Transactions;

//https://xrpl.org/transaction-types.html

namespace Xrpl.Client.Json.Converters
{
    /// <summary> Transaction json Converter </summary>
    public class TransactionResponseConverter : JsonConverter<ITransactionResponse>
    {
        /// <summary>
        /// Writes an <see cref="ITransactionResponse"/> to JSON.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, ITransactionResponse value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            // Remove this converter to avoid infinite recursion
            JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
            for (int i = innerOptions.Converters.Count - 1; i >= 0; i--)
            {
                if (innerOptions.Converters[i] is TransactionResponseConverter)
                    innerOptions.Converters.RemoveAt(i);
            }

            JsonSerializer.Serialize(writer, value, value.GetType(), innerOptions);
        }

        /// <summary>
        /// create <see cref="ITransactionResponse"/> by TransactionType discriminator
        /// </summary>
        public static ITransactionResponse Create(string transactionType)
        {
            return transactionType switch
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
                "AMMClawback" => new AMMClawBackResponse(),

                "Batch" => new BatchResponse(),

                "MPTokenAuthorize" => new MPTokenAuthorizeResponse(),
                "MPTokenIssuanceCreate" => new MPTokenIssuanceCreateResponse(),
                "MPTokenIssuanceDestroy" => new MPTokenIssuanceDestroyResponse(),
                "MPTokenIssuanceSet" => new MPTokenIssuanceSetResponse(),

                "OracleSet" => new OracleSetResponse(),
                "OracleDelete" => new OracleDeleteResponse(),
                "DIDSet" => new DIDSetResponse(),
                "DIDDelete" => new DIDDeleteResponse(),
                "PermissionedDomainSet" => new PermissionedDomainSetResponse(),
                "PermissionedDomainDelete" => new PermissionedDomainDeleteResponse(),
                "CredentialCreate" => new CredentialCreateResponse(),
                "CredentialAccept" => new CredentialAcceptResponse(),
                "CredentialDelete" => new CredentialDeleteResponse(),

                "XChainCreateBridge" => new XChainCreateBridgeResponse(),
                "XChainModifyBridge" => new XChainModifyBridgeResponse(),
                "XChainCreateClaimID" => new XChainCreateClaimIDResponse(),
                "XChainCommit" => new XChainCommitResponse(),
                "XChainClaim" => new XChainClaimResponse(),
                "XChainAccountCreateCommit" => new XChainAccountCreateCommitResponse(),
                "XChainAddClaimAttestation" => new XChainAddClaimAttestationResponse(),
                "XChainAddAccountCreateAttestation" => new XChainAddAccountCreateAttestationResponse(),

                "VaultCreate" => new VaultCreateResponse(),
                "VaultSet" => new VaultSetResponse(),
                "VaultDelete" => new VaultDeleteResponse(),
                "VaultDeposit" => new VaultDepositResponse(),
                "VaultWithdraw" => new VaultWithdrawResponse(),
                "VaultClawback" => new VaultClawbackResponse(),

                "LoanBrokerSet" => new LoanBrokerSetResponse(),
                "LoanBrokerDelete" => new LoanBrokerDeleteResponse(),
                "LoanBrokerCoverDeposit" => new LoanBrokerCoverDepositResponse(),
                "LoanBrokerCoverWithdraw" => new LoanBrokerCoverWithdrawResponse(),
                "LoanBrokerCoverClawback" => new LoanBrokerCoverClawbackResponse(),
                "LoanSet" => new LoanSetResponse(),
                "LoanDelete" => new LoanDeleteResponse(),
                "LoanManage" => new LoanManageResponse(),
                "LoanPay" => new LoanPayResponse(),

                "DelegateSet" => new DelegateSetResponse(),
                "LedgerStateFix" => new LedgerStateFixResponse(),

                //_ => throw new Exception("Can't create transaction type" + transactionType)
                _ => new TransactionResponseUnknown(),
            };
        }

        /// <summary>
        /// Private sentinel type for unknown transaction types.
        /// Using a distinct type avoids the cached converter mapping for TransactionResponse
        /// in System.Text.Json's shared TypeInfoResolver, which causes infinite recursion.
        /// </summary>
        private class TransactionResponseUnknown : TransactionResponse, ITransactionResponse
        {
        }

        /// <summary> read  <see cref="ITransactionResponse"/>   from json object </summary>
        public override ITransactionResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            string transactionType = root.TryGetProperty("TransactionType", out JsonElement ttEl)
                ? ttEl.GetString()
                : null;

            ITransactionResponse transaction = Create(transactionType);
            string rawJson = root.GetRawText();

            // Remove this converter to avoid infinite recursion
            JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
            for (int i = innerOptions.Converters.Count - 1; i >= 0; i--)
            {
                if (innerOptions.Converters[i] is TransactionResponseConverter)
                    innerOptions.Converters.RemoveAt(i);
            }

            try
            {
                return (ITransactionResponse)JsonSerializer.Deserialize(rawJson, transaction.GetType(), innerOptions);
            }
            catch (JsonException)
            {
                return transaction;
            }
        }

        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert) => typeof(ITransactionResponse).IsAssignableFrom(typeToConvert);
    }
}
