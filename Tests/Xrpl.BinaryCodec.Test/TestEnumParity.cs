using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Enums;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib
{
    [TestClass]
    public class TestEnumParity
    {

        [TestMethod]
        public void TestTransactionType_AllNamesResolvable()
        {
            string[] expectedNames = {
                "Payment", "EscrowCreate", "EscrowFinish", "AccountSet",
                "EscrowCancel", "SetRegularKey", "OfferCreate", "OfferCancel",
                "TicketCreate", "SignerListSet", "PaymentChannelCreate",
                "PaymentChannelFund", "PaymentChannelClaim", "CheckCreate",
                "CheckCash", "CheckCancel", "DepositPreauth", "TrustSet",
                "AccountDelete", "NFTokenMint", "NFTokenBurn",
                "NFTokenCreateOffer", "NFTokenCancelOffer", "NFTokenAcceptOffer",
                "Clawback", "AMMCreate", "AMMDeposit", "AMMWithdraw",
                "AMMVote", "AMMBid", "AMMDelete", "AMMClawback",
                "XChainCreateClaimID", "XChainCommit", "XChainClaim",
                "XChainAccountCreateCommit", "XChainAddClaimAttestation",
                "XChainAddAccountCreateAttestation", "XChainModifyBridge",
                "XChainCreateBridge", "DIDSet", "DIDDelete",
                "OracleSet", "OracleDelete", "LedgerStateFix",
                "MPTokenIssuanceCreate", "MPTokenIssuanceDestroy",
                "MPTokenIssuanceSet", "MPTokenAuthorize",
                "CredentialCreate", "CredentialAccept", "CredentialDelete",
                "NFTokenModify", "PermissionedDomainSet", "PermissionedDomainDelete",
                "DelegateSet", "VaultCreate", "VaultSet", "VaultDelete",
                "VaultDeposit", "VaultWithdraw", "VaultClawback", "Batch",
                "EnableAmendment", "SetFee", "UNLModify",
                "LoanBrokerSet", "LoanBrokerDelete", "LoanBrokerCoverDeposit",
                "LoanBrokerCoverWithdraw", "LoanBrokerCoverClawback",
                "LoanSet", "LoanDelete", "LoanManage", "LoanPay"
            };

            foreach (string name in expectedNames)
            {
                Assert.IsTrue(TransactionType.Values.Has(name),
                    $"TransactionType '{name}' not found in Values.");
            }
        }

        [TestMethod]
        public void TestTransactionType_OrdinalLookup()
        {
            TransactionType payment = TransactionType.Values["Payment"];
            Assert.AreEqual(0, payment.Ordinal);

            TransactionType ammCreate = TransactionType.Values["AMMCreate"];
            Assert.AreEqual(35, ammCreate.Ordinal);
        }

        [TestMethod]
        public void TestLedgerEntryType_AllNamesResolvable()
        {
            string[] expectedNames = {
                "AccountRoot", "DirectoryNode", "RippleState", "Offer",
                "LedgerHashes", "Amendments", "FeeSettings",
                "Escrow", "PayChannel", "Check", "DepositPreauth",
                "NegativeUNL", "NFTokenOffer", "NFTokenPage",
                "AMM", "Bridge", "XChainOwnedClaimID",
                "XChainOwnedCreateAccountClaimID", "Oracle", "DID",
                "MPTokenIssuance", "MPToken", "Credential",
                "PermissionedDomain", "SignerList", "Ticket",
                "Delegate", "Vault", "LoanBroker", "Loan"
            };

            foreach (string name in expectedNames)
            {
                Assert.IsTrue(LedgerEntryType.Values.Has(name),
                    $"LedgerEntryType '{name}' not found in Values.");
            }
        }

        [TestMethod]
        public void TestLedgerEntryType_OrdinalLookup()
        {
            LedgerEntryType accountRoot = LedgerEntryType.Values["AccountRoot"];
            Assert.AreEqual(97, accountRoot.Ordinal);

            LedgerEntryType amm = LedgerEntryType.Values["AMM"];
            Assert.AreEqual(121, amm.Ordinal);
        }

        [TestMethod]
        public void TestEngineResult_SuccessAndFailureCodes()
        {
            Assert.IsTrue(EngineResult.Values.Has("tesSUCCESS"));
            Assert.IsTrue(EngineResult.Values.Has("tecCLAIM"));
            Assert.IsTrue(EngineResult.Values.Has("tecPATH_DRY"));
            Assert.IsTrue(EngineResult.Values.Has("temMALFORMED"));
            Assert.IsTrue(EngineResult.Values.Has("tefFAILURE"));
            Assert.IsTrue(EngineResult.Values.Has("terRETRY"));
            Assert.IsTrue(EngineResult.Values.Has("telLOCAL_ERROR"));
        }

        [TestMethod]
        public void TestEngineResult_ShouldClaimFee()
        {
            EngineResult success = (EngineResult)EngineResult.Values["tesSUCCESS"];
            Assert.IsTrue(success.ShouldClaimFee());

            EngineResult tecClaim = (EngineResult)EngineResult.Values["tecCLAIM"];
            Assert.IsTrue(tecClaim.ShouldClaimFee());

            EngineResult temMalformed = (EngineResult)EngineResult.Values["temMALFORMED"];
            Assert.IsFalse(temMalformed.ShouldClaimFee());
        }

        [TestMethod]
        public void TestEngineResult_NewXChainCodes()
        {
            string[] xchainCodes = {
                "tecXCHAIN_BAD_TRANSFER_ISSUE", "tecXCHAIN_NO_CLAIM_ID",
                "tecXCHAIN_BAD_CLAIM_ID", "tecXCHAIN_CLAIM_NO_QUORUM",
                "tecXCHAIN_PROOF_UNKNOWN_KEY", "tecXCHAIN_CREATE_ACCOUNT_NONXRP_ISSUE",
                "tecXCHAIN_WRONG_CHAIN", "tecXCHAIN_REWARD_MISMATCH",
                "tecXCHAIN_NO_SIGNERS_LIST", "tecXCHAIN_SENDING_ACCOUNT_MISMATCH",
                "tecXCHAIN_INSUFF_CREATE_AMOUNT", "tecXCHAIN_ACCOUNT_CREATE_PAST",
                "tecXCHAIN_ACCOUNT_CREATE_TOO_MANY", "tecXCHAIN_PAYMENT_FAILED",
                "tecXCHAIN_SELF_COMMIT", "tecXCHAIN_BAD_PUBLIC_KEY_ACCOUNT_PAIR",
                "tecXCHAIN_CREATE_ACCOUNT_DISABLED"
            };

            foreach (string code in xchainCodes)
            {
                Assert.IsTrue(EngineResult.Values.Has(code),
                    $"EngineResult '{code}' not found in Values.");
            }
        }

        [TestMethod]
        public void TestEngineResult_NewAmmCodes()
        {
            string[] ammCodes = {
                "tecAMM_BALANCE", "tecAMM_FAILED", "tecAMM_INVALID_TOKENS",
                "tecAMM_EMPTY", "tecAMM_NOT_EMPTY", "tecAMM_ACCOUNT"
            };

            foreach (string code in ammCodes)
            {
                Assert.IsTrue(EngineResult.Values.Has(code),
                    $"EngineResult '{code}' not found in Values.");
            }
        }
    }
}
