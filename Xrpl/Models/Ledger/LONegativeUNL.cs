using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Xrpl.Models.Ledger
{
    /// https://github.com/XRPLF/xrpl.js/blob/98f8223b23def3229a42e56d39db42d8a65f506b/packages/xrpl/src/models/ledger/NegativeUNL.ts#L3
    /// <summary>
    /// The NegativeUNL object type contains the current status of the Negative UNL,
    /// a list of trusted validators currently believed to be offline.
    /// </summary>
    public class LONegativeUNL : BaseLedgerEntry
    {
        public LONegativeUNL()
        {
            LedgerEntryType = LedgerEntryType.NegativeUNL;
        }
        /// <summary>
        /// A list of trusted validators that are currently disabled.
        /// </summary>
        public List<DisabledValidator> DisabledValidators { get; set; }
        /// <summary>
        /// The public key of a trusted validator that is scheduled to be disabled in the next flag ledger.
        /// </summary>
        public string ValidatorToDisable { get; set; }
        /// <summary>
        /// The public key of a trusted validator in the Negative UNL that is scheduled to be re-enabled in the next flag ledger.
        /// </summary>
        public string ValidatorToReEnable { get; set; }

        /// <summary>
        /// The identifying hash of the transaction that most recently modified this object.
        /// </summary>
        [JsonPropertyName("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// The index of the ledger that contains the transaction that most recently modified this object.
        /// </summary>
        [JsonPropertyName("PreviousTxnLgrSeq")]
        public uint? PreviousTxnLgrSeq { get; set; }
    }
    public interface IDisabledValidator
    {
        public long FirstLedgerSequence { get; set; }
        public string PublicKey { get; set; }
    }
    public class DisabledValidator : IDisabledValidator
    {
        public long FirstLedgerSequence { get; set; }
        public string PublicKey { get; set; }
    }

}