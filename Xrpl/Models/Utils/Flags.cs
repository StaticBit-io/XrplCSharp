using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Enums;
using Xrpl.Models.Transactions;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/utils/flags.ts

namespace Xrpl.Models.Utils
{
    public class Flags
    {
        /// <summary>
        /// Sets a transaction's flags to its numeric representation.
        /// </summary>
        /// <param name="tx"> A transaction to set its flags to its numeric representation.</param>
        public static void SetTransactionFlagsToNumber(Dictionary<string, object> tx)
        {
            if (!tx.TryGetValue("Flags", out var Flags))
            {
                tx.Add("Flags", 0u);
                return;
            }
            switch (Flags)
            {
                case Enum:
                    tx["Flags"] = (uint)Flags;
                    return;
                case null:
                    tx["Flags"] = 0u;
                    return;
                case uint:
                    return;
                // Dictionaries built by DictionaryObjectConverter hold JSON numbers as int/long/ulong;
                // they are numeric flags already and must not fall through to the map lookup below,
                // which returns 0 for anything that is not a flag dictionary.
                case int or long or ulong or short or ushort or byte or sbyte:
                    object widened = Flags is ulong ? Flags : Convert.ToInt64(Flags, CultureInfo.InvariantCulture);
                    if (!Transactions.Common.TryGetUInt32(widened, out uint numeric))
                        throw new ValidationException($"Flags value {Flags} is not a valid UInt32");
                    tx["Flags"] = numeric;
                    return;
                default:
                    tx["Flags"] = tx["TransactionType"] switch
                    {
                        "AccountSet" => ConvertAccountSetFlagsToNumber(Flags),
                        //TransactionType.AccountDelete => expr,
                        //TransactionType.CheckCancel => expr,
                        //TransactionType.CheckCash => expr,
                        //TransactionType.CheckCreate => expr,
                        //TransactionType.DepositPreauth => expr,
                        //TransactionType.EscrowCancel => expr,
                        //TransactionType.EscrowCreate => expr,
                        //TransactionType.EscrowFinish => expr,
                        //TransactionType.NFTokenAcceptOffer => expr,
                        //TransactionType.NFTokenBurn => expr,
                        //TransactionType.NFTokenCancelOffer => expr,
                        //TransactionType.NFTokenCreateOffer => expr,
                        //TransactionType.NFTokenMint => expr,
                        //TransactionType.OfferCancel => expr,
                        "OfferCreate" => ConvertOfferCreateFlagsToNumber(Flags),
                        "Payment" => ConvertPaymentTransactionFlagsToNumber(Flags),
                        "PaymentChannelClaim" => ConvertPaymentChannelClaimFlagsToNumber(Flags),
                        //TransactionType.PaymentChannelCreate => expr,
                        //TransactionType.PaymentChannelFund => expr,
                        //TransactionType.SetRegularKey => expr,
                        //TransactionType.SignerListSet => expr,
                        //TransactionType.TicketCreate => expr,
                        "TrustSet" => ConvertTrustSetFlagsToNumber(Flags),
                        "AMMDeposit" => ConvertAMMDepositFlagsToNumber(Flags),
                        "AMMWithdraw" => ConvertAMMWithdrawFlagsToNumber(Flags),
                        _ => 0
                    };
                    break;
            }
        }
        public static uint ConvertAMMDepositFlagsToNumber(object flags)
        {
            if (flags is not Dictionary<string, object> flag)
                return 0;
            return ReduceFlags(flag, Enum.GetValues<AMMDepositFlags>().ToDictionary(c => c.ToString(), c => (uint)c));
        }
        public static uint ConvertAMMWithdrawFlagsToNumber(object flags)
        {
            if (flags is not Dictionary<string, object> flag)
                return 0;
            return ReduceFlags(flag, Enum.GetValues<AMMWithdrawFlags>().ToDictionary(c => c.ToString(), c => (uint)c));
        }
        /// <summary>
        /// AccountSet transaction flags (tf*), as xrpl.js <c>AccountSetTfFlags</c>. The asf values of
        /// <see cref="AccountSetAsfFlags"/> are small integers that belong in SetFlag/ClearFlag, not in
        /// Flags: OR-ing them into Flags would set unrelated bits.
        /// </summary>
        private static readonly Dictionary<string, uint> AccountSetTfFlags = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["tfRequireDestTag"] = 0x00010000,
            ["tfOptionalDestTag"] = 0x00020000,
            ["tfRequireAuth"] = 0x00040000,
            ["tfOptionalAuth"] = 0x00080000,
            ["tfDisallowXRP"] = 0x00100000,
            ["tfAllowXRP"] = 0x00200000,
            ["tfInnerBatchTxn"] = (uint)XrplGlobalFlags.tfInnerBatchTxn,
        };

        public static uint ConvertAccountSetFlagsToNumber(object flags)
        {
            if (flags is not Dictionary<string, object> flag)
                return 0;
            return ReduceFlags(flag, AccountSetTfFlags);
        }

        public static uint ConvertOfferCreateFlagsToNumber(object flags)
        {
            if (flags is not Dictionary<string, object> flag)
                return 0;
            return ReduceFlags(flag, Enum.GetValues<OfferCreateFlags>().ToDictionary(c => c.ToString(), c => (uint)c) );
        }

        public static uint ConvertPaymentChannelClaimFlagsToNumber(object flags)
        {
            if (flags is not Dictionary<string, object> flag)
                return 0;
            return ReduceFlags(flag, Enum.GetValues<PaymentChannelClaimFlags>().ToDictionary(c => c.ToString(), c => (uint)c));
        }

        public static uint ConvertPaymentTransactionFlagsToNumber(object flags)
        {
            if (flags is not Dictionary<string, object> flag)
                return 0;
            return ReduceFlags(flag, Enum.GetValues<PaymentFlags>().ToDictionary(c => c.ToString(), c => (uint)c));
        }

        public static uint ConvertTrustSetFlagsToNumber(object flags)
        {
            if (flags is not Dictionary<string, object> flag)
                return 0;
            return ReduceFlags(flag, Enum.GetValues<TrustSetFlags>().ToDictionary(c => c.ToString(), c => (uint)c));
        }

        public static uint ReduceFlags(Dictionary<string, object> flags, Dictionary<string, uint> flagEnum)
        {
            return flags.Select(p => p.Key).Aggregate(0u, (resultFlags, f) =>
            {
                if (!flagEnum.TryGetValue(f, out uint flag))
                {
                    throw new ValidationException($"flag {flags} doesn't exist in flagEnum: {flagEnum}");
                }

                return flags[f] is true ? resultFlags | flag : resultFlags;
            });
        }
    }
}