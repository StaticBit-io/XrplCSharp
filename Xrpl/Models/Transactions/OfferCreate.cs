using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/offerCreate.ts

namespace Xrpl.Models.Transactions
{

    /// <summary>
    /// Transaction Flags for an OfferCreate Transaction.
    /// </summary>
    [Flags]
    public enum OfferCreateFlags : uint
    {
        /// <summary>
        /// batch inner transaction
        /// </summary>
        tfInnerBatchTxn = XrplGlobalFlags.tfInnerBatchTxn,

        /// <summary>
        /// If enabled, the offer does not consume offers that exactly match it, and instead becomes an Offer object in the ledger.<br/>
        /// It still consumes offers that cross it.
        /// 65536
        /// </summary>
        tfPassive = 0x00010000,
        /// <summary>
        /// Treat the offer as an Immediate or Cancel order.<br/>
        /// If enabled, the offer never becomes a ledger object: it only tries to match existing offers in the ledger.<br/>
        /// If the offer cannot match any offers immediately, it executes "successfully" without trading any currency.<br/>
        /// In this case, the transaction has the result code tesSUCCESS, but creates no Offer objects in the ledger.
        /// 131072
        /// </summary>
        tfImmediateOrCancel = 0x00020000,
        /// <summary>
        /// Treat the offer as a Fill or Kill order.<br/>
        /// Only try to match existing offers in the ledger, and only do so if the entire TakerPays quantity can be obtained.<br/>
        /// If the fix1578 amendment is enabled and the offer cannot be executed when placed, the transaction has the result code tecKILLED;<br/>
        /// otherwise, the transaction uses the result code tesSUCCESS even when it was killed without trading any currency.
        /// 262144
        /// </summary>
        tfFillOrKill = 0x00040000,
        /// <summary>
        /// Exchange the entire TakerGets amount, even if it means obtaining more than the TakerPays amount in exchange.
        /// 524288
        /// </summary>
        tfSell = 0x00080000,
        /// <summary>
        /// Make this a hybrid offer that can use both a permissioned DEX and the open DEX.
        /// The DomainID field must be provided when using this flag.
        /// 1048576
        /// </summary>
        tfHybrid = 0x00100000
    }

    /// <inheritdoc cref="IOfferCreate" />
    public class OfferCreate : TransactionRequest, IOfferCreate
    {
        public OfferCreate()
        {
            TransactionType = TransactionType.OfferCreate;
        }
        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Expiration { get; set; }
        /// <inheritdoc />
        public new OfferCreateFlags? Flags
        {
            get => base.Flags.HasValue ? (OfferCreateFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        public uint? OfferSequence { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency TakerGets { get; set; }
        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency TakerPays { get; set; }
        /// <inheritdoc />
        public string DomainID { get; set; }
    }

    /// <summary>
    /// An OfferCreate transaction is effectively a limit order.<br/>
    /// It defines an  intent to exchange currencies, and creates an Offer object if not completely.<br/>
    /// Fulfilled when placed.<br/>
    /// Offers can be partially fulfilled.
    /// </summary>
    public interface IOfferCreate : ITransactionCommon
    {
        /// <summary>
        /// Time after which the offer is no longer active, in seconds since the.<br/>
        /// Ripple Epoch.
        /// </summary>
        DateTime? Expiration { get; set; }
        /// <summary>
        /// Transaction Flags for an OfferCreate Transaction.
        /// </summary>
        new OfferCreateFlags? Flags { get; set; }
        /// <summary>
        /// An offer to delete first, specified in the same way as OfferCancel.
        /// </summary>
        uint? OfferSequence { get; set; }
        /// <summary>
        /// The amount and type of currency being provided by the offer creator.
        /// </summary>
        Currency TakerGets { get; set; }
        /// <summary>
        /// The amount and type of currency being requested by the offer creator.
        /// </summary>
        Currency TakerPays { get; set; }
        /// <summary>
        /// The domain that the offer must be a part of. Required for permissioned DEX offers.
        /// </summary>
        string DomainID { get; set; }

    }

    /// <inheritdoc cref="IOfferCreate" />
    public class OfferCreateResponse : TransactionResponse, IOfferCreate
    {
        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Expiration { get; set; }

        /// <inheritdoc />
        public new OfferCreateFlags? Flags
        {
            get => base.Flags.HasValue ? (OfferCreateFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        public uint? OfferSequence { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency TakerGets { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency TakerPays { get; set; }

        /// <inheritdoc />
        public string DomainID { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a OfferCreate at runtime.
        /// </summary>
        /// <param name="tx"> A OfferCreate Transaction.</param>
        /// <exception cref="ValidationException">When the OfferCreate is malformed.</exception>
        public static async Task ValidateOfferCreate(Dictionary<string, dynamic> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("TakerGets", out var TakerGets) || TakerGets is null)
                throw new ValidationException("OfferCreate: missing field TakerGets");
            if (!tx.TryGetValue("TakerPays", out var TakerPays) || TakerPays is null)
                throw new ValidationException("OfferCreate: missing field TakerPays");

            if (TakerGets is not string && !Common.IsAmount(TakerGets))
                throw new ValidationException("OfferCreate: invalid TakerGets");
            if (TakerPays is not string && !Common.IsAmount(TakerPays))
                throw new ValidationException("OfferCreate: invalid TakerPays");

            if (tx.TryGetValue("Expiration", out var Expiration) && Expiration is not uint { })
                throw new ValidationException("OfferCreate: invalid Expiration");
            if (tx.TryGetValue("OfferSequence", out var OfferSequence) && OfferSequence is not uint { })
                throw new ValidationException("OfferCreate: invalid OfferSequence");

            if (tx.TryGetValue("DomainID", out var domainId) && domainId is not null)
            {
                if (domainId is not string domainIdStr)
                    throw new ValidationException("OfferCreate: DomainID must be a string");
                if (domainIdStr.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(domainIdStr, "^[0-9A-Fa-f]{64}$"))
                    throw new ValidationException("OfferCreate: DomainID must be a 64-character hexadecimal string (256-bit hash)");
            }

            if (tx.TryGetValue("Flags", out var flags) && flags is uint flagValue)
            {
                bool hasTfHybrid = (flagValue & (uint)OfferCreateFlags.tfHybrid) != 0;
                if (hasTfHybrid && (domainId is null || domainId is not string || string.IsNullOrEmpty((string)domainId)))
                {
                    throw new ValidationException("OfferCreate: tfHybrid flag cannot be set if DomainID is not present");
                }
            }
        }
    }

}
