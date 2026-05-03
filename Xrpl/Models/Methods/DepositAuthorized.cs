// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/depositAuthorized.ts

using System.Collections.Generic;

using Newtonsoft.Json;

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// The <c>deposit_authorized</c> command indicates whether one account is authorized to send payments
    /// directly to another. See Deposit Authorization for information on how to require authorization to deliver
    /// money to an account. Returns a <see cref="DepositAuthorized"/>.
    /// </summary>
    /// <code>
    /// {
    ///   "id": 1,
    ///   "command": "deposit_authorized",
    ///   "source_account": "rEhxGqkqPPSxQ3P25J66ft5TwpzV14k2de",
    ///   "destination_account": "rsUiUMpnrgxQp24dJYZDhmV4bE3aBtQyt8",
    ///   "ledger_index": "validated"
    /// }
    /// </code>
    public class DepositAuthorizedRequest : BaseLedgerRequest
    {
        public DepositAuthorizedRequest()
        {
            Command = "deposit_authorized";
        }

        public DepositAuthorizedRequest(string sourceAccount, string destinationAccount)
            : this()
        {
            SourceAccount = sourceAccount;
            DestinationAccount = destinationAccount;
        }

        /// <summary>
        /// The sender of a possible payment.
        /// </summary>
        [JsonProperty("source_account")]
        public string SourceAccount { get; set; }

        /// <summary>
        /// The recipient of a possible payment.
        /// </summary>
        [JsonProperty("destination_account")]
        public string DestinationAccount { get; set; }

        /// <summary>
        /// (Optional, XLS-70) Hex-encoded object IDs (64 hex chars each) of accepted Credentials objects
        /// to use for credential-based deposit preauthorization.
        /// </summary>
        [JsonProperty("credentials", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Credentials { get; set; }
    }

    /// <summary>
    /// Response expected from a <see cref="DepositAuthorizedRequest"/>.
    /// </summary>
    public class DepositAuthorized
    {
        /// <summary>
        /// Whether the specified source account is authorized to send payments directly to the destination account.
        /// If true, depositing to the destination account is either possible without authorization, the source account
        /// has the necessary preauthorization, or the destination account does not require Deposit Authorization.
        /// </summary>
        [JsonProperty("deposit_authorized")]
        public bool IsDepositAuthorized { get; set; }

        /// <summary>
        /// The destination account specified in the request.
        /// </summary>
        [JsonProperty("destination_account")]
        public string DestinationAccount { get; set; }

        /// <summary>
        /// The source account specified in the request.
        /// </summary>
        [JsonProperty("source_account")]
        public string SourceAccount { get; set; }

        /// <summary>
        /// (May be omitted) The hash of the ledger version used to generate this response.
        /// </summary>
        [JsonProperty("ledger_hash", NullValueHandling = NullValueHandling.Ignore)]
        public string LedgerHash { get; set; }

        /// <summary>
        /// (May be omitted) The ledger index of the ledger version used to generate this response.
        /// </summary>
        [JsonProperty("ledger_index", NullValueHandling = NullValueHandling.Ignore)]
        public uint? LedgerIndex { get; set; }

        /// <summary>
        /// (May be omitted) The ledger index of the current in-progress ledger version, used when no ledger version
        /// was specified in the request.
        /// </summary>
        [JsonProperty("ledger_current_index", NullValueHandling = NullValueHandling.Ignore)]
        public uint? LedgerCurrentIndex { get; set; }

        /// <summary>
        /// (May be omitted) If true, the information comes from a validated ledger version.
        /// </summary>
        [JsonProperty("validated", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Validated { get; set; }

        /// <summary>
        /// (Optional, XLS-70) The credentials specified in the request, echoed back.
        /// </summary>
        [JsonProperty("credentials", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Credentials { get; set; }
    }
}
