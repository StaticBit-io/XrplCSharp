using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Utils;

namespace Xrpl.Models.Transactions
{
    public static partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a TrustSet at runtime.
        /// </summary>
        /// <param name="tx"> A TrustSet Transaction.</param>
        /// <exception cref="ValidationException">When the TrustSet is malformed.</exception>
        public static void Validate(Dictionary<string, object> tx)
        {
            tx.TryGetValue("TransactionType", out var type);

            if(type is null)
                throw new ValidationException("Object does not have a `TransactionType`");
            if(type is not string)
                throw new ValidationException("Object's `TransactionType` is not a string");

            //var value = JsonConvert.DeserializeObject<TransactionCommon>(tx as dynamic);

            // eslint-disable-next-line @typescript-eslint/consistent-type-assertions -- okay here
            Flags.SetTransactionFlagsToNumber(tx);
            
            switch (type)
            {
                case "AccountDelete":
                    ValidateAccountDelete(tx);
                    break;

                case "AccountSet":
                    ValidateAccountSet(tx);
                    break;

                case "CheckCancel":
                    ValidateCheckCancel(tx);
                    break;

                case "CheckCash":
                    ValidateCheckCash(tx);
                    break;

                case "CheckCreate":
                    ValidateCheckCreate(tx);
                    break;

                case "DepositPreauth":
                    ValidateDepositPreauth(tx);
                    break;

                case "EscrowCancel":
                    ValidateEscrowCancel(tx);
                    break;

                case "EscrowCreate":
                    ValidateEscrowCreate(tx);
                    break;

                case "EscrowFinish":
                    ValidateEscrowFinish(tx);
                    break;

                case "NFTokenAcceptOffer":
                    ValidateNFTokenAcceptOffer(tx);
                    break;

                case "NFTokenBurn":
                    ValidateNFTokenBurn(tx);
                    break;

                case "NFTokenCancelOffer":
                    ValidateNFTokenCancelOffer(tx);
                    break;

                case "NFTokenCreateOffer":
                    ValidateNFTokenCreateOffer(tx);
                    break;

                case "NFTokenMint":
                    ValidateNFTokenMint(tx);
                    break;
                case "NFTokenModify":
                    ValidateNFTokenModify(tx);
                    break;

                case "OfferCancel":
                    ValidateOfferCancel(tx);
                    break;

                case "OfferCreate":
                    ValidateOfferCreate(tx);
                    break;

                case "Payment":
                    ValidatePayment(tx);
                    break;

                case "PaymentChannelClaim":
                    ValidatePaymentChannelClaim(tx);
                    break;

                case "PaymentChannelCreate":
                    ValidatePaymentChannelCreate(tx);
                    break;

                case "PaymentChannelFund":
                    ValidatePaymentChannelFund(tx);
                    break;

                case "SetRegularKey":
                    ValidateSetRegularKey(tx);
                    break;

                case "SignerListSet":
                    ValidateSignerListSet(tx);
                    break;

                case "TicketCreate":
                    ValidateTicketCreate(tx);
                    break;

                case "TrustSet":
                    ValidateTrustSet(tx);
                    break;
                case "AMMBid":
                    ValidateAMMBid(tx);
                    break;
                case "AMMDeposit":
                    ValidateAMMDeposit(tx);
                    break;
                case "AMMCreate":
                    ValidateAMMCreate(tx);
                    break;
                case "AMMDelete":
                    ValidateAMMDelete(tx);
                    break;
                case "AMMVote":
                    ValidateAMMVote(tx);
                    break;
                case "AMMWithdraw":
                    ValidateAMMWithdraw(tx);
                    break;
                case "Batch":
                    ValidateBatch(tx);
                    break;
                case "MPTokenIssuanceCreate":
                    ValidateMPTokenIssuanceCreate(tx);
                    break;
                case "MPTokenIssuanceDestroy":
                    ValidateMPTokenIssuanceDestroy(tx);
                    break;
                case "MPTokenIssuanceSet":
                    ValidateMPTokenIssuanceSet(tx);
                    break;
                case "MPTokenAuthorize":
                    ValidateMPTokenAuthorize(tx);
                    break;
                case "OracleSet":
                    ValidateOracleSet(tx);
                    break;
                case "OracleDelete":
                    ValidateOracleDelete(tx);
                    break;
                case "Clawback":
                    ValidateClawBack(tx);
                    break;
                case "AMMClawback":
                    ValidateAMMClawBack(tx);
                    break;
                case "DIDSet":
                    ValidateDIDSet(tx);
                    break;
                case "DIDDelete":
                    ValidateDIDDelete(tx);
                    break;
                case "PermissionedDomainSet":
                    ValidatePermissionedDomainSet(tx);
                    break;
                case "PermissionedDomainDelete":
                    ValidatePermissionedDomainDelete(tx);
                    break;
                case "CredentialCreate":
                    ValidateCredentialCreate(tx);
                    break;
                case "CredentialAccept":
                    ValidateCredentialAccept(tx);
                    break;
                case "CredentialDelete":
                    ValidateCredentialDelete(tx);
                    break;

                case "XChainCreateBridge":
                    ValidateXChainCreateBridge(tx);
                    break;
                case "XChainModifyBridge":
                    ValidateXChainModifyBridge(tx);
                    break;
                case "XChainCreateClaimID":
                    ValidateXChainCreateClaimID(tx);
                    break;
                case "XChainCommit":
                    ValidateXChainCommit(tx);
                    break;
                case "XChainClaim":
                    ValidateXChainClaim(tx);
                    break;
                case "XChainAccountCreateCommit":
                    ValidateXChainAccountCreateCommit(tx);
                    break;
                case "XChainAddClaimAttestation":
                    ValidateXChainAddClaimAttestation(tx);
                    break;
                case "XChainAddAccountCreateAttestation":
                    ValidateXChainAddAccountCreateAttestation(tx);
                    break;

                case "VaultCreate":
                    ValidateVaultCreate(tx);
                    break;
                case "VaultSet":
                    ValidateVaultSet(tx);
                    break;
                case "VaultDelete":
                    ValidateVaultDelete(tx);
                    break;
                case "VaultDeposit":
                    ValidateVaultDeposit(tx);
                    break;
                case "VaultWithdraw":
                    ValidateVaultWithdraw(tx);
                    break;
                case "VaultClawback":
                    ValidateVaultClawback(tx);
                    break;

                case "LoanBrokerSet":
                    ValidateLoanBrokerSet(tx);
                    break;
                case "LoanBrokerDelete":
                    ValidateLoanBrokerDelete(tx);
                    break;
                case "LoanBrokerCoverDeposit":
                    ValidateLoanBrokerCoverDeposit(tx);
                    break;
                case "LoanBrokerCoverWithdraw":
                    ValidateLoanBrokerCoverWithdraw(tx);
                    break;
                case "LoanBrokerCoverClawback":
                    ValidateLoanBrokerCoverClawback(tx);
                    break;
                case "LoanSet":
                    ValidateLoanSet(tx);
                    break;
                case "LoanDelete":
                    ValidateLoanDelete(tx);
                    break;
                case "LoanManage":
                    ValidateLoanManage(tx);
                    break;
                case "LoanPay":
                    ValidateLoanPay(tx);
                    break;

                case "DelegateSet":
                    ValidateDelegateSet(tx);
                    break;
                case "LedgerStateFix":
                    ValidateLedgerStateFix(tx);
                    break;

                case "SponsorshipSet":
                    ValidateSponsorshipSet(tx);
                    break;
                case "SponsorshipTransfer":
                    ValidateSponsorshipTransfer(tx);
                    break;

                case "ConfidentialMPTConvert":
                    ValidateConfidentialMPTConvert(tx);
                    break;
                case "ConfidentialMPTMergeInbox":
                    ValidateConfidentialMPTMergeInbox(tx);
                    break;
                case "ConfidentialMPTConvertBack":
                    ValidateConfidentialMPTConvertBack(tx);
                    break;
                case "ConfidentialMPTSend":
                    ValidateConfidentialMPTSend(tx);
                    break;
                case "ConfidentialMPTClawback":
                    ValidateConfidentialMPTClawback(tx);
                    break;

                default:
                    throw new ValidationException($"Invalid field TransactionType: {type}");
            }
        }
    }
}
