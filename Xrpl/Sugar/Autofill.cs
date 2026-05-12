using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.AddressCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Utils;

using static Xrpl.AddressCodec.XrplAddressCodec;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/sugar/autofill.ts

namespace Xrpl.Sugar
{
    public class AddressNTag
    {
        public string ClassicAddress { get; set; }
        public uint? Tag { get; set; }
    }

    public static class AutofillSugar
    {
        const int LEDGER_OFFSET = 20;
        
        /// <summary>
        /// Devnet has minimum fee 0.0000001 instead of 0.000001 (7 digits after dot).
        /// This multiplier corrects the base fee for devnet transactions.
        /// </summary>
        const int DEVNET_FEE_CORRECTION_MULTIPLIER = 12;
        
        /// <summary>
        /// Batch transactions have a base fee multiplier of 3.
        /// </summary>
        const int BATCH_BASE_FEE_MULTIPLIER = 3;


        /// <summary>
        /// Autofills fields in a transaction. This will set `Sequence`, `Fee`,
        /// `lastLedgerSequence` according to the current state of the server this Client
        /// is connected to. It also converts all X-Addresses to classic addresses and
        /// flags interfaces into numbers.
        /// </summary>
        /// <param name="client">A client.</param>
        /// <param name="transaction">A {@link Transaction} in JSON format</param>
        /// <param name="signersCount">The expected number of signers for this transaction. Only used for multisigned transactions.</param>
        /// <returns>The autofilled transaction.</returns>
        public static async Task<Dictionary<string, object>> Autofill(this IXrplClient client, Dictionary<string, object> transaction, int? signersCount, CancellationToken cancellationToken = default)
        {

            Dictionary<string, object> tx = transaction;

            tx.SetValidAddresses();

            //Flags.SetTransactionFlagsToNumber(tx);
            List<Task> promises = new List<Task>();
            bool hasTT = tx.TryGetValue("TransactionType", out var tt);
            string txType = $"{tt}";
            if (!tx.ContainsKey("Sequence") && txType != "Batch")
            {
                promises.Add(client.SetNextValidSequenceNumber(tx, cancellationToken));
            }
            if (!tx.ContainsKey("Fee"))
            {
                promises.Add(client.CalculateFeePerTransactionType(tx, signersCount ?? 0, cancellationToken));
            }
            if (!tx.ContainsKey("LastLedgerSequence"))
            {
                promises.Add(client.SetLatestValidatedLedgerSequence(tx, cancellationToken));
            }
            else if(tx.TryGetValue("LastLedgerSequence", out var lastLedgerValue) && lastLedgerValue is 0u or 0UL or 0L or 0)
            {
                tx.Remove("LastLedgerSequence");
            }
            if (txType == "Batch")
            {
                promises.Add(client.NormalizeBatchTransaction(tx, cancellationToken));
            }
            await Task.WhenAll(promises);
            //string jsonData = JsonConvert.SerializeObject(tx);
            return tx;
        }


        public static void SetValidAddresses(this Dictionary<string, object> tx)
        {
            tx.ValidateAccountAddress("Account", "SourceTag");
            if (tx.ContainsKey("Destination"))
            {
                tx.ValidateAccountAddress("Destination", "DestinationTag");
            }

            // DepositPreauth:
            tx.ConvertToClassicAddress("Authorize");
            tx.ConvertToClassicAddress("Unauthorize");
            // EscrowCancel, EscrowFinish:
            tx.ConvertToClassicAddress("Owner");
            // SetRegularKey:
            tx.ConvertToClassicAddress("RegularKey");
        }

        public static void ValidateAccountAddress(this Dictionary<string, object> tx, string accountField, string tagField)
        {
            // if X-address is given, convert it to classic address
            var ainfo = tx.TryGetValue(accountField, out var aField);

            AddressNTag classicAccount = GetClassicAccountAndTag((string)aField, null);
            tx[accountField] = classicAccount.ClassicAddress;

            var tinfo = tx.TryGetValue(tagField, out var tField);

            // XRPL: Does bool or int. Smells.
            if (classicAccount.Tag != null)
            {
                if (tField != null && (int)tField != classicAccount.Tag)
                {
                    throw new ValidationException($"The {tagField}, if present, must match the tag of the {accountField} X - address");
                }
                // eslint-disable-next-line no-param-reassign -- param reassign is safe
                tx[tagField] = classicAccount.Tag;
            }
        }

        public static AddressNTag GetClassicAccountAndTag(this string account, uint? expectedTag)
        {
            if (!account.StartsWith('r') && XrplAddressCodec.IsValidXAddress(account))
            {
                CodecAddress codecAddress = XrplAddressCodec.XAddressToClassicAddress(account);
                if (expectedTag != null && codecAddress.Tag != expectedTag)
                {
                    throw new ValidationException("address includes a tag that does not match the tag specified in the transaction");
                }
                return new AddressNTag { ClassicAddress = codecAddress.ClassicAddress, Tag = codecAddress.Tag };
            }
            return new AddressNTag { ClassicAddress = account, Tag = expectedTag };
        }

        public static void ConvertToClassicAddress(this Dictionary<string, object> tx, string fieldName)
        {
            if (tx.ContainsKey(fieldName))
            {
                string account = (string)tx[fieldName];
                if (account is string)
                {
                    AddressNTag addressntag = account.GetClassicAccountAndTag(null);
                    tx[fieldName] = addressntag.ClassicAddress;
                }
            }
        }

        public static async Task<uint> SetNextValidSequenceNumber(this IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Current);
            AccountInfoRequest request = new AccountInfoRequest((string)tx["Account"]) { LedgerIndex = index };
            AccountInfo data = await client.AccountInfo(request, cancellationToken);
            tx.TryAdd("Sequence", data.AccountData.Sequence);
            return data.AccountData.Sequence;
        }

        public static async Task<BigInteger> FetchReserveFee(this IXrplClient client, CancellationToken cancellationToken = default)
        {
            ServerStateRequest request = new ServerStateRequest();
            ServerState data = await client.ServerState(request, cancellationToken);
            uint? fee = data.State.ValidatedLedger.ReserveInc;

            if (fee == null)
            {
                throw new XrplException("Could not fetch Owner Reserve.");
            }
            return BigInteger.Parse(fee.Value.ToString());
        }

        public static async Task CalculateFeePerTransactionType(this IXrplClient client, Dictionary<string, object> tx, int signersCount = 0, CancellationToken cancellationToken = default)
        {
            var netFeeXRP = await client.GetFeeXrp(cancellationToken: cancellationToken);
            var netFeeDrops = XrpConversion.XrpToDrops(netFeeXRP);
            var baseFee = new BigInteger(Math.Floor(decimal.Parse(netFeeDrops, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)));
            
            // Devnet returns fees ~12x lower than mainnet. Detect by checking if baseFee < 10 drops.
            if (baseFee < 10)
            {
                baseFee *= DEVNET_FEE_CORRECTION_MULTIPLIER;
            }

            var transactionType = (string)tx["TransactionType"];
            var calculatedFee = await CalculateBaseFeeForType(client, tx, transactionType, baseFee, netFeeDrops, cancellationToken);
            var signerFee = CalculateMultisigFee(netFeeDrops, signersCount);
            
            calculatedFee += signerFee;
            BigInteger totalFee;
            if (!string.IsNullOrWhiteSpace(client.maxFeeXRP))
            {
                var maxFeeDrops = XrpConversion.XrpToDrops(client.maxFeeXRP);
                var maxFeeBI = new BigInteger(Math.Floor(decimal.Parse(maxFeeDrops, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)));
                totalFee = transactionType == "AccountDelete"
                    ? calculatedFee
                    : BigInteger.Min(calculatedFee, maxFeeBI);
            }
            else
            {
                totalFee = calculatedFee;
            }
            tx.TryAdd("Fee", Math.Ceiling((decimal)totalFee).ToString());
        }

        private static async Task<BigInteger> CalculateBaseFeeForType(
            IXrplClient client, 
            Dictionary<string, object> tx, 
            string transactionType, 
            BigInteger baseFee, 
            string netFeeDrops,
            CancellationToken cancellationToken = default)
        {
            return transactionType switch
            {
                "EscrowFinish" when tx.TryGetValue("Fulfillment", out _) => CalculateEscrowFinishFee(tx, netFeeDrops),
                "Batch" => await CalculateBatchFee(client, tx, baseFee, cancellationToken),
                // LoanSet requires CounterpartySignature (~150 bytes extra).
                // Fee formula: baseFee * (1 + 1 counterparty signer) = baseFee * 2
                "LoanSet" => baseFee * 2,
                _ when IsReserveFeeTxNeed(tx) => await FetchReserveFee(client, cancellationToken),
                _ => baseFee
            };
        }

        /// <summary>
        /// Calculates fee for EscrowFinish with Fulfillment.
        /// Formula: 10 drops × (33 + (Fulfillment size in bytes / 16))
        /// </summary>
        private static BigInteger CalculateEscrowFinishFee(Dictionary<string, object> tx, string netFeeDrops)
        {
            decimal fulfillmentBytesSize = Math.Ceiling((decimal)((string)tx["Fulfillment"]).Length / 2);
            decimal multiplier = 33 + (fulfillmentBytesSize / 16);
            var scaled = ScaleValueDecimal(netFeeDrops, multiplier);
            return new BigInteger(Math.Ceiling(scaled));
        }

        /// <summary>
        /// Calculates fee for Batch transactions.
        /// Base fee is multiplied by BATCH_BASE_FEE_MULTIPLIER plus fee for each inner transaction.
        /// </summary>
        private static async Task<BigInteger> CalculateBatchFee(IXrplClient client, Dictionary<string, object> tx, BigInteger baseFee, CancellationToken cancellationToken = default)
        {
            var calculatedFee = baseFee * BATCH_BASE_FEE_MULTIPLIER;
            
            if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
            {
                throw new ValidationException("Batch transaction must have RawTransactions field.");
            }

            IEnumerable<object> items = rawTransactions switch
            {
                JsonArray ja => JsonSerializer.Deserialize<List<object>>(ja.ToJsonString(), XrplJsonOptions.Default)!,
                IEnumerable<object> ie => ie,
                _ => new List<object> { rawTransactions }
            };

            foreach (var inner in items)
            {
                if (!TryGetInnerFieldsAsDict(inner, out var innerTx))
                    throw new ArgumentNullException(nameof(inner), "RawTransaction not found or invalid.");

                calculatedFee += IsReserveFeeTxNeed(innerTx) 
                    ? await FetchReserveFee(client, cancellationToken) 
                    : baseFee;
            }

            return calculatedFee;
        }

        /// <summary>
        /// Calculates additional fee for multi-signed transactions.
        /// Formula: baseFeeDrops × signersCount (added to base fee already calculated elsewhere)
        /// Total multisig fee = baseFee × (1 + signersCount)
        /// </summary>
        private static BigInteger CalculateMultisigFee(string netFeeDrops, int signersCount)
        {
            if (signersCount <= 0)
                return BigInteger.Zero;

            var scaled = ScaleValueDecimal(netFeeDrops, signersCount);
            return new BigInteger(scaled);
        }
        private static bool TryGetInnerFieldsAsDict(object item, out Dictionary<string, object> dict)
        {
            dict = null!;

            // Приводим к JsonObject максимально рано
            JsonObject entry = item as JsonObject
                ?? JsonNode.Parse(JsonSerializer.Serialize(item, XrplJsonOptions.Default))?.AsObject();
            if (entry == null) return false;

            // Достаём RawTransaction
            JsonNode rawNode = entry["RawTransaction"];
            if (rawNode == null) return false;
            JsonObject raw = rawNode as JsonObject
                ?? JsonNode.Parse(rawNode.ToJsonString())?.AsObject();
            if (raw == null) return false;

            // В словарь
            var tmp = JsonSerializer.Deserialize<Dictionary<string, object>>(raw.ToJsonString(), XrplJsonOptions.Default);
            if (tmp == null) return false;

            dict = tmp;
            return true;
        }
        private static bool IsReserveFeeTxNeed(Dictionary<string, object> tx)
        {
            string txType = $"{tx["TransactionType"]}";
            return txType 
                is nameof(TransactionType.AccountDelete) 
                or nameof(TransactionType.AMMCreate) 
                or nameof(TransactionType.LedgerStateFix);
        }

        public static decimal ScaleValueDecimal(string value, decimal multiplier)
        {
            return decimal.Parse(value, CultureInfo.InvariantCulture) * multiplier;
        }

        public static async Task SetLatestValidatedLedgerSequence(this IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            uint ledgerSequence = await client.GetLedgerIndex(cancellationToken);
            tx.TryAdd("LastLedgerSequence", ledgerSequence + LEDGER_OFFSET);
        }

        public static async Task CheckAccountDeleteBlockers(this IXrplClient client, Dictionary<string, object> tx, CancellationToken cancellationToken = default)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Validated);
            AccountObjectsRequest request = new AccountObjectsRequest((string)tx["Account"])
            {
                LedgerIndex = index,
                DeletionBlockersOnly = true,
            };
            AccountObjects response = await client.AccountObjects(request, cancellationToken);
            TaskCompletionSource task = new TaskCompletionSource();
            if (response.AccountObjectList.Count > 0)
            {
                task.TrySetException(new XrplException($"Account {(string)tx["Account"]} cannot be deleted; there are Escrows, PayChannels, RippleStates, or Checks associated with the account."));
            }
            task.TrySetResult();
        }
    }
}