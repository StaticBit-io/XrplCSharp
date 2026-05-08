using Xrpl.BinaryCodec.Enums;

namespace Xrpl.BinaryCodec.Enums
{
    public class EngineResult : SerializedEnumItem<byte>
    {
        public class EngineResultValues : SerializedEnumeration<EngineResult, byte>{}
        public static EngineResultValues Values = new EngineResultValues();
        private readonly string _description;
        public EngineResult(string name, int ordinal, string description) : base(name, ordinal)
        {
            _description = description;
        }
        private static EngineResult Add(string name, int ordinal, string description)
        {
            return Values.AddEnum(new EngineResult(name, ordinal, description));
        }

        // ─── tel: Local errors ───────────────────────────────────────────────

        public static readonly EngineResult telLOCAL_ERROR = Add(nameof(telLOCAL_ERROR), -399, "Local failure.");
        public static readonly EngineResult telBAD_DOMAIN = Add(nameof(telBAD_DOMAIN), -398, "Domain too long.");
        public static readonly EngineResult telBAD_PATH_COUNT = Add(nameof(telBAD_PATH_COUNT), -397, "Malformed: Too many paths.");
        public static readonly EngineResult telBAD_PUBLIC_KEY = Add(nameof(telBAD_PUBLIC_KEY), -396, "Public key too long.");
        public static readonly EngineResult telFAILED_PROCESSING = Add(nameof(telFAILED_PROCESSING), -395, "Failed to correctly process transaction.");
        public static readonly EngineResult telINSUF_FEE_P = Add(nameof(telINSUF_FEE_P), -394, "Fee insufficient.");
        public static readonly EngineResult telNO_DST_PARTIAL = Add(nameof(telNO_DST_PARTIAL), -393, "Partial payment to create account not allowed.");
        public static readonly EngineResult telCAN_NOT_QUEUE = Add(nameof(telCAN_NOT_QUEUE), -392, "Can not queue at this time.");
        public static readonly EngineResult telCAN_NOT_QUEUE_BALANCE = Add(nameof(telCAN_NOT_QUEUE_BALANCE), -391, "Can not queue at this time: insufficient balance to pay all queued fees.");
        public static readonly EngineResult telCAN_NOT_QUEUE_BLOCKS = Add(nameof(telCAN_NOT_QUEUE_BLOCKS), -390, "Can not queue at this time: would block later queued transaction(s).");
        public static readonly EngineResult telCAN_NOT_QUEUE_BLOCKED = Add(nameof(telCAN_NOT_QUEUE_BLOCKED), -389, "Can not queue at this time: blocking transaction in queue.");
        public static readonly EngineResult telCAN_NOT_QUEUE_FEE = Add(nameof(telCAN_NOT_QUEUE_FEE), -388, "Can not queue at this time: fee insufficient to replace queued transaction.");
        public static readonly EngineResult telCAN_NOT_QUEUE_FULL = Add(nameof(telCAN_NOT_QUEUE_FULL), -387, "Can not queue at this time: queue is full.");
        public static readonly EngineResult telWRONG_NETWORK = Add(nameof(telWRONG_NETWORK), -386, "Transaction specifies a Network ID that differs from that of the local node.");
        public static readonly EngineResult telREQUIRES_NETWORK_ID = Add(nameof(telREQUIRES_NETWORK_ID), -385, "Transaction must include a Network ID.");
        public static readonly EngineResult telNETWORK_ID_MAKES_TX_NON_CANONICAL = Add(nameof(telNETWORK_ID_MAKES_TX_NON_CANONICAL), -384, "Transactions with a Network ID are not valid on this network.");
        public static readonly EngineResult telENV_RPC_FAILED = Add(nameof(telENV_RPC_FAILED), -383, "Failed to apply envelope.");

        // ─── tem: Malformed transaction ──────────────────────────────────────

        public static readonly EngineResult temMALFORMED = Add(nameof(temMALFORMED), -299, "Malformed transaction.");
        public static readonly EngineResult temBAD_AMOUNT = Add(nameof(temBAD_AMOUNT), -298, "Can only send positive amounts.");
        public static readonly EngineResult temBAD_CURRENCY = Add(nameof(temBAD_CURRENCY), -297, "Malformed: Bad currency.");
        public static readonly EngineResult temBAD_EXPIRATION = Add(nameof(temBAD_EXPIRATION), -296, "Malformed: Bad expiration.");
        public static readonly EngineResult temBAD_FEE = Add(nameof(temBAD_FEE), -295, "Invalid fee, negative or not XRP.");
        public static readonly EngineResult temBAD_ISSUER = Add(nameof(temBAD_ISSUER), -294, "Malformed: Bad issuer.");
        public static readonly EngineResult temBAD_LIMIT = Add(nameof(temBAD_LIMIT), -293, "Limits must be non-negative.");
        public static readonly EngineResult temBAD_OFFER = Add(nameof(temBAD_OFFER), -292, "Malformed: Bad offer.");
        public static readonly EngineResult temBAD_PATH = Add(nameof(temBAD_PATH), -291, "Malformed: Bad path.");
        public static readonly EngineResult temBAD_PATH_LOOP = Add(nameof(temBAD_PATH_LOOP), -290, "Malformed: Loop in path.");
        public static readonly EngineResult temBAD_REGKEY = Add(nameof(temBAD_REGKEY), -289, "Malformed: Bad regular key.");
        public static readonly EngineResult temBAD_SEND_XRP_LIMIT = Add(nameof(temBAD_SEND_XRP_LIMIT), -288, "Malformed: Limit quality is not allowed for XRP to XRP.");
        public static readonly EngineResult temBAD_SEND_XRP_MAX = Add(nameof(temBAD_SEND_XRP_MAX), -287, "Malformed: Send max is not allowed for XRP to XRP.");
        public static readonly EngineResult temBAD_SEND_XRP_NO_DIRECT = Add(nameof(temBAD_SEND_XRP_NO_DIRECT), -286, "Malformed: No Ripple direct is not allowed for XRP to XRP.");
        public static readonly EngineResult temBAD_SEND_XRP_PARTIAL = Add(nameof(temBAD_SEND_XRP_PARTIAL), -285, "Malformed: Partial payment is not allowed for XRP to XRP.");
        public static readonly EngineResult temBAD_SEND_XRP_PATHS = Add(nameof(temBAD_SEND_XRP_PATHS), -284, "Malformed: Paths are not allowed for XRP to XRP.");
        public static readonly EngineResult temBAD_SEQUENCE = Add(nameof(temBAD_SEQUENCE), -283, "Malformed: Sequence is not in the past.");
        public static readonly EngineResult temBAD_SIGNATURE = Add(nameof(temBAD_SIGNATURE), -282, "Malformed: Bad signature.");
        public static readonly EngineResult temBAD_SRC_ACCOUNT = Add(nameof(temBAD_SRC_ACCOUNT), -281, "Malformed: Bad source account.");
        public static readonly EngineResult temBAD_TRANSFER_RATE = Add(nameof(temBAD_TRANSFER_RATE), -280, "Malformed: Bad transfer rate.");
        public static readonly EngineResult temDST_IS_SRC = Add(nameof(temDST_IS_SRC), -279, "Destination may not be source.");
        public static readonly EngineResult temDST_NEEDED = Add(nameof(temDST_NEEDED), -278, "Destination not specified.");
        public static readonly EngineResult temINVALID = Add(nameof(temINVALID), -277, "The transaction is ill-formed.");
        public static readonly EngineResult temINVALID_FLAG = Add(nameof(temINVALID_FLAG), -276, "The transaction has an invalid flag.");
        public static readonly EngineResult temREDUNDANT = Add(nameof(temREDUNDANT), -275, "Sends same currency to self.");
        public static readonly EngineResult temRIPPLE_EMPTY = Add(nameof(temRIPPLE_EMPTY), -274, "PathSet with no paths.");
        public static readonly EngineResult temDISABLED = Add(nameof(temDISABLED), -273, "The transaction requires logic that is currently disabled.");
        public static readonly EngineResult temBAD_SIGNER = Add(nameof(temBAD_SIGNER), -272, "Malformed: Bad signer.");
        public static readonly EngineResult temBAD_QUORUM = Add(nameof(temBAD_QUORUM), -271, "Malformed: Bad quorum.");
        public static readonly EngineResult temBAD_WEIGHT = Add(nameof(temBAD_WEIGHT), -270, "Malformed: Bad weight.");
        public static readonly EngineResult temBAD_TICK_SIZE = Add(nameof(temBAD_TICK_SIZE), -269, "Malformed: Bad tick size.");
        public static readonly EngineResult temINVALID_ACCOUNT_ID = Add(nameof(temINVALID_ACCOUNT_ID), -268, "Malformed: Invalid account ID.");
        public static readonly EngineResult temCANNOT_PREAUTH_SELF = Add(nameof(temCANNOT_PREAUTH_SELF), -267, "Malformed: Cannot preauthorize self.");
        public static readonly EngineResult temINVALID_COUNT = Add(nameof(temINVALID_COUNT), -266, "Malformed: Invalid count.");
        public static readonly EngineResult temUNCERTAIN = Add(nameof(temUNCERTAIN), -265, "In process of determining result. Never returned.");
        public static readonly EngineResult temUNKNOWN = Add(nameof(temUNKNOWN), -264, "The transaction requires logic not implemented yet.");
        public static readonly EngineResult temSEQ_AND_TICKET = Add(nameof(temSEQ_AND_TICKET), -263, "Transaction cannot have both a non-zero Sequence and a TicketSequence.");
        public static readonly EngineResult temBAD_NFTOKEN_TRANSFER_FEE = Add(nameof(temBAD_NFTOKEN_TRANSFER_FEE), -262, "Malformed: Bad NFToken transfer fee.");
        public static readonly EngineResult temBAD_AMM_TOKENS = Add(nameof(temBAD_AMM_TOKENS), -261, "Malformed: Bad AMM tokens.");
        public static readonly EngineResult temXCHAIN_EQUAL_DOOR_ACCOUNTS = Add(nameof(temXCHAIN_EQUAL_DOOR_ACCOUNTS), -260, "Malformed: Equal door accounts.");
        public static readonly EngineResult temXCHAIN_BAD_PROOF = Add(nameof(temXCHAIN_BAD_PROOF), -259, "Malformed: Bad cross-chain proof.");
        public static readonly EngineResult temXCHAIN_BRIDGE_BAD_ISSUES = Add(nameof(temXCHAIN_BRIDGE_BAD_ISSUES), -258, "Malformed: Bad cross-chain bridge issues.");
        public static readonly EngineResult temXCHAIN_BRIDGE_NONDOOR_OWNER = Add(nameof(temXCHAIN_BRIDGE_NONDOOR_OWNER), -257, "Malformed: Cross-chain bridge non-door owner.");
        public static readonly EngineResult temXCHAIN_BRIDGE_BAD_MIN_ACCOUNT_CREATE_AMOUNT = Add(nameof(temXCHAIN_BRIDGE_BAD_MIN_ACCOUNT_CREATE_AMOUNT), -256, "Malformed: Bad min account create amount.");
        public static readonly EngineResult temXCHAIN_BRIDGE_BAD_REWARD_AMOUNT = Add(nameof(temXCHAIN_BRIDGE_BAD_REWARD_AMOUNT), -255, "Malformed: Bad reward amount.");
        public static readonly EngineResult temEMPTY_DID = Add(nameof(temEMPTY_DID), -254, "Malformed: Empty DID.");
        public static readonly EngineResult temARRAY_EMPTY = Add(nameof(temARRAY_EMPTY), -253, "Malformed: Array is empty.");
        public static readonly EngineResult temARRAY_TOO_LARGE = Add(nameof(temARRAY_TOO_LARGE), -252, "Malformed: Array is too large.");
        public static readonly EngineResult temBAD_TRANSFER_FEE = Add(nameof(temBAD_TRANSFER_FEE), -251, "Malformed: Bad transfer fee.");
        public static readonly EngineResult temINVALID_INNER_BATCH = Add(nameof(temINVALID_INNER_BATCH), -250, "Malformed: Invalid inner batch transaction.");

        // ─── tef: Transaction engine failure ─────────────────────────────────

        public static readonly EngineResult tefFAILURE = Add(nameof(tefFAILURE), -199, "Failed to apply.");
        public static readonly EngineResult tefALREADY = Add(nameof(tefALREADY), -198, "The exact transaction was already in this ledger.");
        public static readonly EngineResult tefBAD_ADD_AUTH = Add(nameof(tefBAD_ADD_AUTH), -197, "Not authorized to add account.");
        public static readonly EngineResult tefBAD_AUTH = Add(nameof(tefBAD_AUTH), -196, "Transaction's public key is not authorized.");
        public static readonly EngineResult tefBAD_LEDGER = Add(nameof(tefBAD_LEDGER), -195, "Ledger in unexpected state.");
        public static readonly EngineResult tefCREATED = Add(nameof(tefCREATED), -194, "Can't add an already created account.");
        public static readonly EngineResult tefEXCEPTION = Add(nameof(tefEXCEPTION), -193, "Unexpected program state.");
        public static readonly EngineResult tefINTERNAL = Add(nameof(tefINTERNAL), -192, "Internal error.");
        public static readonly EngineResult tefNO_AUTH_REQUIRED = Add(nameof(tefNO_AUTH_REQUIRED), -191, "Auth is not required.");
        public static readonly EngineResult tefPAST_SEQ = Add(nameof(tefPAST_SEQ), -190, "This sequence number has already passed.");
        public static readonly EngineResult tefWRONG_PRIOR = Add(nameof(tefWRONG_PRIOR), -189, "This previous transaction does not match.");
        public static readonly EngineResult tefMASTER_DISABLED = Add(nameof(tefMASTER_DISABLED), -188, "Master key is disabled.");
        public static readonly EngineResult tefMAX_LEDGER = Add(nameof(tefMAX_LEDGER), -187, "Ledger sequence too high.");
        public static readonly EngineResult tefBAD_SIGNATURE = Add(nameof(tefBAD_SIGNATURE), -186, "The transaction was multi-signed, but contained a bad signature.");
        public static readonly EngineResult tefBAD_QUORUM = Add(nameof(tefBAD_QUORUM), -185, "The transaction was multi-signed, but the quorum was not met.");
        public static readonly EngineResult tefNOT_MULTI_SIGNING = Add(nameof(tefNOT_MULTI_SIGNING), -184, "Account has no appropriate list of multi-signers.");
        public static readonly EngineResult tefBAD_AUTH_MASTER = Add(nameof(tefBAD_AUTH_MASTER), -183, "Auth for unclaimed account needs correct master key.");
        public static readonly EngineResult tefINVARIANT_FAILED = Add(nameof(tefINVARIANT_FAILED), -182, "Fee claim violated invariants for the transaction.");
        public static readonly EngineResult tefTOO_BIG = Add(nameof(tefTOO_BIG), -181, "Transaction is too big.");
        public static readonly EngineResult tefNO_TICKET = Add(nameof(tefNO_TICKET), -180, "Ticket is not in the ledger.");
        public static readonly EngineResult tefNFTOKEN_IS_NOT_TRANSFERABLE = Add(nameof(tefNFTOKEN_IS_NOT_TRANSFERABLE), -179, "NFToken is not transferable.");
        public static readonly EngineResult tefINVALID_LEDGER_FIX_TYPE = Add(nameof(tefINVALID_LEDGER_FIX_TYPE), -178, "Invalid ledger fix type.");

        // ─── ter: Transaction engine retry ───────────────────────────────────

        public static readonly EngineResult terRETRY = Add(nameof(terRETRY), -99, "Retry transaction.");
        public static readonly EngineResult terFUNDS_SPENT = Add(nameof(terFUNDS_SPENT), -98, "Can't set password, password set funds already spent.");
        public static readonly EngineResult terINSUF_FEE_B = Add(nameof(terINSUF_FEE_B), -97, "Account balance can't pay fee.");
        public static readonly EngineResult terNO_ACCOUNT = Add(nameof(terNO_ACCOUNT), -96, "The source account does not exist.");
        public static readonly EngineResult terNO_AUTH = Add(nameof(terNO_AUTH), -95, "Not authorized to hold IOUs.");
        public static readonly EngineResult terNO_LINE = Add(nameof(terNO_LINE), -94, "No such line.");
        public static readonly EngineResult terOWNERS = Add(nameof(terOWNERS), -93, "Non-zero owner count.");
        public static readonly EngineResult terPRE_SEQ = Add(nameof(terPRE_SEQ), -92, "Missing/inapplicable prior transaction.");
        public static readonly EngineResult terLAST = Add(nameof(terLAST), -91, "Process last.");
        public static readonly EngineResult terNO_RIPPLE = Add(nameof(terNO_RIPPLE), -90, "Path does not permit rippling.");
        public static readonly EngineResult terQUEUED = Add(nameof(terQUEUED), -89, "Held until escalated fee drops.");
        public static readonly EngineResult terPRE_TICKET = Add(nameof(terPRE_TICKET), -88, "Ticket is not yet in the ledger.");
        public static readonly EngineResult terNO_AMM = Add(nameof(terNO_AMM), -87, "AMM not found.");
        public static readonly EngineResult terADDRESS_COLLISION = Add(nameof(terADDRESS_COLLISION), -86, "Address collision.");
        public static readonly EngineResult terNO_DELEGATE_PERMISSION = Add(nameof(terNO_DELEGATE_PERMISSION), -85, "No delegate permission.");

        // ─── tes: Transaction engine success ─────────────────────────────────

        public static readonly EngineResult tesSUCCESS = Add(nameof(tesSUCCESS), 0, "The transaction was applied.");

        // ─── tec: Transaction engine claimed (fee claimed, not applied) ──────

        public static readonly EngineResult tecCLAIM = Add(nameof(tecCLAIM), 100, "Fee claimed. Sequence used. No action.");
        public static readonly EngineResult tecPATH_PARTIAL = Add(nameof(tecPATH_PARTIAL), 101, "Path could not send full amount.");
        public static readonly EngineResult tecUNFUNDED_ADD = Add(nameof(tecUNFUNDED_ADD), 102, "Insufficient XRP balance for WalletAdd.");
        public static readonly EngineResult tecUNFUNDED_OFFER = Add(nameof(tecUNFUNDED_OFFER), 103, "Insufficient balance to fund created offer.");
        public static readonly EngineResult tecUNFUNDED_PAYMENT = Add(nameof(tecUNFUNDED_PAYMENT), 104, "Insufficient XRP balance to send.");
        public static readonly EngineResult tecFAILED_PROCESSING = Add(nameof(tecFAILED_PROCESSING), 105, "Failed to correctly process transaction.");
        public static readonly EngineResult tecDIR_FULL = Add(nameof(tecDIR_FULL), 121, "Can not add entry to full directory.");
        public static readonly EngineResult tecINSUF_RESERVE_LINE = Add(nameof(tecINSUF_RESERVE_LINE), 122, "Insufficient reserve to add trust line.");
        public static readonly EngineResult tecINSUF_RESERVE_OFFER = Add(nameof(tecINSUF_RESERVE_OFFER), 123, "Insufficient reserve to create offer.");
        public static readonly EngineResult tecNO_DST = Add(nameof(tecNO_DST), 124, "Destination does not exist. Send XRP to create it.");
        public static readonly EngineResult tecNO_DST_INSUF_XRP = Add(nameof(tecNO_DST_INSUF_XRP), 125, "Destination does not exist. Too little XRP sent to create it.");
        public static readonly EngineResult tecNO_LINE_INSUF_RESERVE = Add(nameof(tecNO_LINE_INSUF_RESERVE), 126, "No such line. Too little reserve to create it.");
        public static readonly EngineResult tecNO_LINE_REDUNDANT = Add(nameof(tecNO_LINE_REDUNDANT), 127, "Can't set non-existent line to default.");
        public static readonly EngineResult tecPATH_DRY = Add(nameof(tecPATH_DRY), 128, "Path could not send partial amount.");
        public static readonly EngineResult tecUNFUNDED = Add(nameof(tecUNFUNDED), 129, "One of _ADD, _OFFER, or _SEND. Deprecated.");
        public static readonly EngineResult tecNO_ALTERNATIVE_KEY = Add(nameof(tecNO_ALTERNATIVE_KEY), 130, "The operation would remove the ability to sign transactions with the account.");
        public static readonly EngineResult tecNO_REGULAR_KEY = Add(nameof(tecNO_REGULAR_KEY), 131, "Regular key is not set.");
        public static readonly EngineResult tecOWNERS = Add(nameof(tecOWNERS), 132, "Non-zero owner count.");
        public static readonly EngineResult tecNO_ISSUER = Add(nameof(tecNO_ISSUER), 133, "Issuer account does not exist.");
        public static readonly EngineResult tecNO_AUTH = Add(nameof(tecNO_AUTH), 134, "Not authorized to hold asset.");
        public static readonly EngineResult tecNO_LINE = Add(nameof(tecNO_LINE), 135, "No such line.");
        public static readonly EngineResult tecINSUFF_FEE = Add(nameof(tecINSUFF_FEE), 136, "Insufficient balance to pay fee.");
        public static readonly EngineResult tecFROZEN = Add(nameof(tecFROZEN), 137, "Asset is frozen.");
        public static readonly EngineResult tecNO_TARGET = Add(nameof(tecNO_TARGET), 138, "Target account does not exist.");
        public static readonly EngineResult tecNO_PERMISSION = Add(nameof(tecNO_PERMISSION), 139, "No permission to perform requested operation.");
        public static readonly EngineResult tecNO_ENTRY = Add(nameof(tecNO_ENTRY), 140, "No matching entry found.");
        public static readonly EngineResult tecINSUFFICIENT_RESERVE = Add(nameof(tecINSUFFICIENT_RESERVE), 141, "Insufficient reserve to complete requested operation.");
        public static readonly EngineResult tecNEED_MASTER_KEY = Add(nameof(tecNEED_MASTER_KEY), 142, "The operation requires the use of the Master Key.");
        public static readonly EngineResult tecDST_TAG_NEEDED = Add(nameof(tecDST_TAG_NEEDED), 143, "A destination tag is required.");
        public static readonly EngineResult tecINTERNAL = Add(nameof(tecINTERNAL), 144, "An internal error has occurred during processing.");
        public static readonly EngineResult tecOVERSIZE = Add(nameof(tecOVERSIZE), 145, "Object exceeded serialization limits.");
        public static readonly EngineResult tecCRYPTOCONDITION_ERROR = Add(nameof(tecCRYPTOCONDITION_ERROR), 146, "Malformed, invalid, or mismatched conditional or fulfillment.");
        public static readonly EngineResult tecINVARIANT_FAILED = Add(nameof(tecINVARIANT_FAILED), 147, "One or more invariants for the transaction were not satisfied.");
        public static readonly EngineResult tecEXPIRED = Add(nameof(tecEXPIRED), 148, "Expiration time is passed.");
        public static readonly EngineResult tecDUPLICATE = Add(nameof(tecDUPLICATE), 149, "Ledger object already exists.");
        public static readonly EngineResult tecKILLED = Add(nameof(tecKILLED), 150, "FillOrKill offer killed.");
        public static readonly EngineResult tecHAS_OBLIGATIONS = Add(nameof(tecHAS_OBLIGATIONS), 151, "The account cannot be deleted because it has obligations.");
        public static readonly EngineResult tecTOO_SOON = Add(nameof(tecTOO_SOON), 152, "It is too early to attempt the requested operation.");
        public static readonly EngineResult tecHOOK_REJECTED = Add(nameof(tecHOOK_REJECTED), 153, "Rejected by hook.");
        public static readonly EngineResult tecMAX_SEQUENCE_REACHED = Add(nameof(tecMAX_SEQUENCE_REACHED), 154, "The maximum sequence number was reached.");
        public static readonly EngineResult tecNO_SUITABLE_NFTOKEN_PAGE = Add(nameof(tecNO_SUITABLE_NFTOKEN_PAGE), 155, "A suitable NFToken page could not be found.");
        public static readonly EngineResult tecNFTOKEN_BUY_SELL_MISMATCH = Add(nameof(tecNFTOKEN_BUY_SELL_MISMATCH), 156, "The specified buy and sell NFToken offers are mismatched.");
        public static readonly EngineResult tecNFTOKEN_OFFER_TYPE_MISMATCH = Add(nameof(tecNFTOKEN_OFFER_TYPE_MISMATCH), 157, "The specified NFToken offer is the wrong type.");
        public static readonly EngineResult tecCANT_ACCEPT_OWN_NFTOKEN_OFFER = Add(nameof(tecCANT_ACCEPT_OWN_NFTOKEN_OFFER), 158, "Cannot accept an NFToken offer from self.");
        public static readonly EngineResult tecINSUFFICIENT_FUNDS = Add(nameof(tecINSUFFICIENT_FUNDS), 159, "Not enough funds available to complete requested transaction.");
        public static readonly EngineResult tecOBJECT_NOT_FOUND = Add(nameof(tecOBJECT_NOT_FOUND), 160, "A requested object could not be found.");
        public static readonly EngineResult tecINSUFFICIENT_PAYMENT = Add(nameof(tecINSUFFICIENT_PAYMENT), 161, "The payment is not sufficient.");
        public static readonly EngineResult tecUNFUNDED_AMM = Add(nameof(tecUNFUNDED_AMM), 162, "Insufficient balance to fund AMM.");
        public static readonly EngineResult tecAMM_BALANCE = Add(nameof(tecAMM_BALANCE), 163, "AMM has invalid balance.");
        public static readonly EngineResult tecAMM_FAILED = Add(nameof(tecAMM_FAILED), 164, "AMM transaction failed.");
        public static readonly EngineResult tecAMM_INVALID_TOKENS = Add(nameof(tecAMM_INVALID_TOKENS), 165, "AMM invalid LP tokens.");
        public static readonly EngineResult tecAMM_EMPTY = Add(nameof(tecAMM_EMPTY), 166, "AMM is empty.");
        public static readonly EngineResult tecAMM_NOT_EMPTY = Add(nameof(tecAMM_NOT_EMPTY), 167, "AMM is not empty.");
        public static readonly EngineResult tecAMM_ACCOUNT = Add(nameof(tecAMM_ACCOUNT), 168, "AMM account error.");
        public static readonly EngineResult tecINCOMPLETE = Add(nameof(tecINCOMPLETE), 169, "Some work was completed, but more submissions required to finish.");
        public static readonly EngineResult tecXCHAIN_BAD_TRANSFER_ISSUE = Add(nameof(tecXCHAIN_BAD_TRANSFER_ISSUE), 170, "Bad cross-chain transfer issue.");
        public static readonly EngineResult tecXCHAIN_NO_CLAIM_ID = Add(nameof(tecXCHAIN_NO_CLAIM_ID), 171, "No cross-chain claim ID.");
        public static readonly EngineResult tecXCHAIN_BAD_CLAIM_ID = Add(nameof(tecXCHAIN_BAD_CLAIM_ID), 172, "Bad cross-chain claim ID.");
        public static readonly EngineResult tecXCHAIN_CLAIM_NO_QUORUM = Add(nameof(tecXCHAIN_CLAIM_NO_QUORUM), 173, "Cross-chain claim does not have quorum.");
        public static readonly EngineResult tecXCHAIN_PROOF_UNKNOWN_KEY = Add(nameof(tecXCHAIN_PROOF_UNKNOWN_KEY), 174, "Unknown key in cross-chain proof.");
        public static readonly EngineResult tecXCHAIN_CREATE_ACCOUNT_NONXRP_ISSUE = Add(nameof(tecXCHAIN_CREATE_ACCOUNT_NONXRP_ISSUE), 175, "Cross-chain create account non-XRP issue.");
        public static readonly EngineResult tecXCHAIN_WRONG_CHAIN = Add(nameof(tecXCHAIN_WRONG_CHAIN), 176, "Cross-chain wrong chain.");
        public static readonly EngineResult tecXCHAIN_REWARD_MISMATCH = Add(nameof(tecXCHAIN_REWARD_MISMATCH), 177, "Cross-chain reward mismatch.");
        public static readonly EngineResult tecXCHAIN_NO_SIGNERS_LIST = Add(nameof(tecXCHAIN_NO_SIGNERS_LIST), 178, "Cross-chain no signers list.");
        public static readonly EngineResult tecXCHAIN_SENDING_ACCOUNT_MISMATCH = Add(nameof(tecXCHAIN_SENDING_ACCOUNT_MISMATCH), 179, "Cross-chain sending account mismatch.");
        public static readonly EngineResult tecXCHAIN_INSUFF_CREATE_AMOUNT = Add(nameof(tecXCHAIN_INSUFF_CREATE_AMOUNT), 180, "Cross-chain insufficient create amount.");
        public static readonly EngineResult tecXCHAIN_ACCOUNT_CREATE_PAST = Add(nameof(tecXCHAIN_ACCOUNT_CREATE_PAST), 181, "Cross-chain account create past.");
        public static readonly EngineResult tecXCHAIN_ACCOUNT_CREATE_TOO_MANY = Add(nameof(tecXCHAIN_ACCOUNT_CREATE_TOO_MANY), 182, "Cross-chain account create too many.");
        public static readonly EngineResult tecXCHAIN_PAYMENT_FAILED = Add(nameof(tecXCHAIN_PAYMENT_FAILED), 183, "Cross-chain payment failed.");
        public static readonly EngineResult tecXCHAIN_SELF_COMMIT = Add(nameof(tecXCHAIN_SELF_COMMIT), 184, "Cross-chain self-commit.");
        public static readonly EngineResult tecXCHAIN_BAD_PUBLIC_KEY_ACCOUNT_PAIR = Add(nameof(tecXCHAIN_BAD_PUBLIC_KEY_ACCOUNT_PAIR), 185, "Cross-chain bad public key account pair.");
        public static readonly EngineResult tecXCHAIN_CREATE_ACCOUNT_DISABLED = Add(nameof(tecXCHAIN_CREATE_ACCOUNT_DISABLED), 186, "Cross-chain create account disabled.");
        public static readonly EngineResult tecEMPTY_DID = Add(nameof(tecEMPTY_DID), 187, "DID is empty.");
        public static readonly EngineResult tecINVALID_UPDATE_TIME = Add(nameof(tecINVALID_UPDATE_TIME), 188, "Invalid update time.");
        public static readonly EngineResult tecTOKEN_PAIR_NOT_FOUND = Add(nameof(tecTOKEN_PAIR_NOT_FOUND), 189, "Token pair not found.");
        public static readonly EngineResult tecARRAY_EMPTY = Add(nameof(tecARRAY_EMPTY), 190, "Array is empty.");
        public static readonly EngineResult tecARRAY_TOO_LARGE = Add(nameof(tecARRAY_TOO_LARGE), 191, "Array is too large.");
        public static readonly EngineResult tecLOCKED = Add(nameof(tecLOCKED), 192, "Locked.");
        public static readonly EngineResult tecBAD_CREDENTIALS = Add(nameof(tecBAD_CREDENTIALS), 193, "Bad credentials.");
        public static readonly EngineResult tecWRONG_ASSET = Add(nameof(tecWRONG_ASSET), 194, "Wrong asset.");
        public static readonly EngineResult tecLIMIT_EXCEEDED = Add(nameof(tecLIMIT_EXCEEDED), 195, "Limit exceeded.");
        public static readonly EngineResult tecPSEUDO_ACCOUNT = Add(nameof(tecPSEUDO_ACCOUNT), 196, "Cannot modify a pseudo account.");
        public static readonly EngineResult tecPRECISION_LOSS = Add(nameof(tecPRECISION_LOSS), 197, "Precision loss.");
        public static readonly EngineResult tecNO_DELEGATE_PERMISSION = Add(nameof(tecNO_DELEGATE_PERMISSION), 198, "No delegate permission.");

        public bool ShouldClaimFee()
        {
            return Ordinal >= 0;
        }
    }
}
