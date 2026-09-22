using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models;
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
            JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<TransactionResponseConverter>(options);

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

                "SponsorshipSet" => new SponsorshipSetResponse(),
                "SponsorshipTransfer" => new SponsorshipTransferResponse(),

                "ConfidentialMPTConvert" => new ConfidentialMPTConvertResponse(),
                "ConfidentialMPTMergeInbox" => new ConfidentialMPTMergeInboxResponse(),
                "ConfidentialMPTConvertBack" => new ConfidentialMPTConvertBackResponse(),
                "ConfidentialMPTSend" => new ConfidentialMPTSendResponse(),
                "ConfidentialMPTClawback" => new ConfidentialMPTClawbackResponse(),

                //_ => throw new Exception("Can't create transaction type" + transactionType)
                _ => new TransactionResponseUnknown(),
            };
        }

        /// <summary>
        /// Private sentinel type for unknown transaction types, including a response that
        /// carries no TransactionType at all.
        /// Using a distinct type avoids the cached converter mapping for TransactionResponse
        /// in System.Text.Json's shared TypeInfoResolver, which causes infinite recursion.
        /// The constructor sets the property because when the field is missing there is
        /// nothing for <see cref="TransactionTypeConverter"/> to read, and the enum's default
        /// is AccountSet - a silently wrong type rather than an unknown one.
        /// </summary>
        private class TransactionResponseUnknown : TransactionResponse, ITransactionResponse
        {
            public TransactionResponseUnknown() => TransactionType = TransactionType.Unknown;
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
            JsonSerializerOptions innerOptions = JsonSerializerOptionsCache.WithoutConverter<TransactionResponseConverter>(options);

            try
            {
                return (ITransactionResponse)JsonSerializer.Deserialize(rawJson, transaction.GetType(), innerOptions);
            }
            catch (JsonException)
            {
                // The failed pass never got to assign TransactionType, and no *Response class sets
                // it in a constructor, so without this the object would carry the enum default -
                // AccountSet, a real and common type - and report itself as a transaction it is not.
                transaction.TransactionType = ParseTransactionType(transactionType);
                return transaction;
            }
        }

        /// <summary>
        /// Resolves the TransactionType discriminator to its enum member, or
        /// <see cref="TransactionType.Unknown"/> when it names no member.
        /// </summary>
        private static TransactionType ParseTransactionType(string transactionType)
        {
            // Enum.TryParse also accepts the decimal form of a member's value, which would turn a
            // discriminator that is not a name at all into whichever member holds that number.
            // Comparing the round trip back to a name rejects those.
            if (Enum.TryParse(transactionType, out TransactionType parsed)
                && string.Equals(parsed.ToString(), transactionType, StringComparison.Ordinal))
            {
                return parsed;
            }

            return TransactionType.Unknown;
        }

        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert) => typeof(ITransactionResponse).IsAssignableFrom(typeToConvert);
    }
}
