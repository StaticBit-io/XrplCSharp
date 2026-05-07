using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/escrowCreate.ts

namespace Xrpl.Models.Transactions
{
    /// <inheritdoc cref="IEscrowCreate" />
    public class EscrowCreate : TransactionRequest, IEscrowCreate, IDestination
    {
        public EscrowCreate()
        {
            TransactionType = TransactionType.EscrowCreate;
        }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? CancelAfter { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? FinishAfter { get; set; }

        /// <inheritdoc />
        public string Condition { get; set; }

        /// <inheritdoc />
        public uint? DestinationTag { get; set; }
    }

    /// <summary>
    /// Sequester XRP or fungible tokens (IOUs, MPTs) until the escrow process either finishes or is canceled. Requires the TokenEscrow amendment for fungible token support.
    /// </summary>
    public interface IEscrowCreate : ITransactionCommon, IDestination
    {
        /// <summary>
        /// The amount to deduct from the sender's balance and set aside in escrow. Can be XRP (in drops, as a string), an IOU token, or an MPT. Must always be a positive value. With the TokenEscrow amendment, this field supports fungible tokens in addition to XRP.
        /// </summary>
        Currency Amount { get; set; }
        /// <summary>
        /// The time, in seconds since the Ripple Epoch, when this escrow expires.<br/>
        /// This value is immutable; the funds can only be returned the sender after.<br/>
        /// this time.
        /// </summary>
        DateTime? CancelAfter { get; set; }
        /// <summary>
        /// Hex value representing a PREIMAGE-SHA-256 crypto-condition.<br/>
        /// The funds can.<br/>
        /// only be delivered to the recipient if this condition is fulfilled.
        /// </summary>
        string Condition { get; set; }
        /// <summary>
        /// Address to receive escrowed funds.
        /// </summary>
        string Destination { get; set; }
        /// <summary>
        /// Arbitrary tag to further specify the destination for this escrowed.<br/>
        /// payment, such as a hosted recipient at the destination address.
        /// </summary>
        uint? DestinationTag { get; set; }
        /// <summary>
        /// The time, in seconds since the Ripple Epoch, when the escrowed XRP can be released to the recipient.<br/>
        /// This value is immutable; the funds cannot move.<br/>
        /// until this time is reached.
        /// </summary>
        DateTime? FinishAfter { get; set; }
    }

    /// <inheritdoc cref="IEscrowCreate" />
    public class EscrowCreateResponse : TransactionResponse, IEscrowCreate, IDestination
    {
        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? CancelAfter { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? FinishAfter { get; set; }

        /// <inheritdoc />
        public string Condition { get; set; }

        /// <inheritdoc />
        public uint? DestinationTag { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a EscrowCreate at runtime.
        /// </summary>
        /// <param name="tx"> A EscrowCreate Transaction.</param>
        /// <exception cref="ValidationException">When the EscrowCreate is malformed.</exception>
        public static async Task ValidateEscrowCreate(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);
            tx.TryGetValue("Amount", out var Amount);

            if (Amount is null)
                throw new ValidationException("EscrowCreate: missing field Amount");

            if (Amount is not string && Amount is not Dictionary<string, object>)
                throw new ValidationException("EscrowCreate: Amount must be a string (XRP) or object (IOU/MPT)");


            tx.TryGetValue("Destination", out var Destination);
            if (Destination is null)
                throw new ValidationException("EscrowCreate: missing field Destination");
            if (Destination is not string)
                throw new ValidationException("EscrowCreate: Destination must be a string");

            tx.TryGetValue("CancelAfter", out var CancelAfter);
            tx.TryGetValue("FinishAfter", out var FinishAfter);
            tx.TryGetValue("Condition", out var Condition);

            if (CancelAfter is null && FinishAfter is null)
                throw new ValidationException("EscrowCreate: Either CancelAfter or FinishAfter must be specified");

            if (FinishAfter is null && Condition is null)
                throw new ValidationException("EscrowCreate: Either Condition or FinishAfter must be specified");

            if (CancelAfter is not null && CancelAfter is not uint)
                throw new ValidationException("EscrowCreate: CancelAfter must be a number");
            if (FinishAfter is not null && FinishAfter is not uint)
                throw new ValidationException("EscrowCreate: FinishAfter must be a number");
            if (Condition is not null && Condition is not string)
                throw new ValidationException("EscrowCreate: Condition must be a string");

            tx.TryGetValue("DestinationTag", out var DestinationTag);
            if (Destination is not null && DestinationTag is not uint)
                throw new ValidationException("EscrowCreate: DestinationTag must be a number");
        }
    }

}
