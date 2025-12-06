using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Xml.Linq;

using Xrpl.AddressCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
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
        /// Autofills fields in a transaction. This will set `Sequence`, `Fee`,
        /// `lastLedgerSequence` according to the current state of the server this Client
        /// is connected to. It also converts all X-Addresses to classic addresses and
        /// flags interfaces into numbers.
        /// </summary>
        /// <param name="client">A client.</param>
        /// <param name="transaction">A {@link Transaction} in JSON format</param>
        /// <param name="signersCount">The expected number of signers for this transaction. Only used for multisigned transactions.</param>
        // <returns>The autofilled transaction.</returns>
        public static async Task<Dictionary<string, dynamic>> Autofill(this IXrplClient client, Dictionary<string, dynamic> transaction, int? signersCount)
        {

            Dictionary<string, dynamic> tx = transaction;

            tx.SetValidAddresses();

            //Flags.SetTransactionFlagsToNumber(tx);
            List<Task> promises = new List<Task>();
            bool hasTT = tx.TryGetValue("TransactionType", out var tt);
            if (!tx.ContainsKey("Sequence") && tt != "Batch")
            {
                promises.Add(client.SetNextValidSequenceNumber(tx));
            }
            if (!tx.ContainsKey("Fee"))
            {
                promises.Add(client.CalculateFeePerTransactionType(tx, signersCount ?? 0));
            }
            if (!tx.ContainsKey("LastLedgerSequence"))
            {
                promises.Add(client.SetLatestValidatedLedgerSequence(tx));
            }
            else if(tx.TryGetValue("LastLedgerSequence", out var lastLedgerValue) && lastLedgerValue is 0u or 0UL or 0L or 0)
            {
                tx.Remove("LastLedgerSequence");
            }
            if (tt == "AccountDelete")
            {
                //todo error here
                //promises.Add(client.CheckAccountDeleteBlockers(tx));
            }
            if (tt == "Batch")
            {
                promises.Add(client.NormolizeBatch(tx));
            }
            await Task.WhenAll(promises);
            //string jsonData = JsonConvert.SerializeObject(tx);
            return tx;
        }


        public static void SetValidAddresses(this Dictionary<string, dynamic> tx)
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

        public static void ValidateAccountAddress(this Dictionary<string, dynamic> tx, string accountField, string tagField)
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
            if (XrplAddressCodec.IsValidXAddress(account))
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

        public static void ConvertToClassicAddress(this Dictionary<string, dynamic> tx, string fieldName)
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

        public static async Task<uint> SetNextValidSequenceNumber(this IXrplClient client, Dictionary<string, dynamic> tx)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Current);
            AccountInfoRequest request = new AccountInfoRequest((string)tx["Account"]) { LedgerIndex = index };
            AccountInfo data = await client.AccountInfo(request);
            tx.TryAdd("Sequence", data.AccountData.Sequence);
            return data.AccountData.Sequence;
        }

        public static async Task NormolizeBatch(this IXrplClient client, Dictionary<string, dynamic> tx)
        {
            if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
                throw new ValidationException("Batch transaction must have RawTransactions field.");

            // 1) NORMALIZATION: bring to List<Dictionary<string,dynamic>> and return to tx
            var raws = rawTransactions switch
            {
                JArray ja => ja.ToObject<List<Dictionary<string, dynamic>>>()
                             ?? new List<Dictionary<string, dynamic>>(),
                IEnumerable ie => ie.Cast<object>()
                                    .Select(o => o as Dictionary<string, dynamic>
                                              ?? JObject.FromObject(o!).ToObject<Dictionary<string, dynamic>>()!)
                                    .ToList(),
                _ => throw new ValidationException("RawTransactions must be array/collection.")
            };
            tx["RawTransactions"] = raws; // <-- critical: now we edit the same object

            // 2) Cache of sequences by accounts
            var nextSeqByAccount = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

            async Task<uint> GetNextSeqForAccountAsync(string account)
            {
                if (nextSeqByAccount.TryGetValue(account, out var seq))
                    return seq;

                var probe = new Dictionary<string, dynamic> { ["Account"] = account };
                await client.SetNextValidSequenceNumber(probe);
                var start = ToUInt(probe["Sequence"]);
                nextSeqByAccount[account] = start;
                return start;
            }
            void Bump(string account)
            {
                if (nextSeqByAccount.TryGetValue(account, out var val))
                    nextSeqByAccount[account] = checked(val + 1);
            }

            // 3) Put Sequence for root Batch (and synchronize the counter to the same account)
            var rootAccount = $"{tx["Account"]}";
            if (!tx.ContainsKey("Sequence") || tx["Sequence"] is null)
            {
                var seq = await GetNextSeqForAccountAsync(rootAccount);
                tx["Sequence"] = seq;
                nextSeqByAccount[rootAccount] = checked(seq + 1);
            }
            else
            {
                var seq = ToUInt(tx["Sequence"]);
                nextSeqByAccount[rootAccount] = checked(seq + 1);
            }

            // 4) Bypass and edit INTERNAL TRANSACTIONS (we work ONLY with dictionaries)
            foreach (var wrapper in raws)
            {
                if (!wrapper.TryGetValue("RawTransaction", out var rawTxObj) || rawTxObj is null)
                    throw new ValidationException("Each item in RawTransactions must contain 'RawTransaction'.");

                // we guarantee a dictionary
                var rawTx = rawTxObj as Dictionary<string, dynamic>
                            ?? JObject.FromObject(rawTxObj).ToObject<Dictionary<string, dynamic>>()!;

                // back in the shell - to accurately lay the dictionary (and not JObject)
                wrapper["RawTransaction"] = rawTx;

                // account inner-tx
                var account = rawTx.TryGetValue("Account", out object accObj)
                    ? accObj?.ToString()
                    : null;
                if (string.IsNullOrWhiteSpace(account))
                    throw new ValidationException("Each RawTransaction must have an 'Account' field.");

                // next sequence for account
                var next = await GetNextSeqForAccountAsync(account);

                // ПРАВКА НА МЕСТЕ — изменения остаются в tx
                rawTx["Sequence"] = next;
                rawTx["Fee"] = "0";

                Bump(account);
            }
        }

        private static uint ToUInt(object? v)
        {
            if (v is null) throw new ValidationException("Sequence is null.");
            return v switch
            {
                uint u => u,
                int i when i >= 0 => (uint)i,
                long l when l >= 0 => checked((uint)l),
                string s => uint.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
                JValue jv => ToUInt(jv.Value),
                _ => Convert.ToUInt32(v, System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        public static async Task<BigInteger> FetchReserveFee(this IXrplClient client)
        {
            ServerStateRequest request = new ServerStateRequest();
            ServerState data = await client.ServerState(request);
            uint? fee = data.State.ValidatedLedger.ReserveInc;

            if (fee == null)
            {
                await Task.FromException(new XrplException("Could not fetch Owner Reserve."));
            }
            return BigInteger.Parse(fee.ToString());
        }

        public static async Task CalculateFeePerTransactionType(this IXrplClient client, Dictionary<string, dynamic> tx, int signersCount = 0)
        {
            var netFeeXRP = await client.GetFeeXrp();
            var netFeeDrops = XrpConversion.XrpToDrops(netFeeXRP);
            var baseFee = BigInteger.Parse(netFeeDrops, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent);
            BigInteger calculatedFee = 0;
            BigInteger signerFee = 0;
            // EscrowFinish Transaction with Fulfillment
            bool has_fulfillment = tx.TryGetValue("Fulfillment", out var Fulfillment);
            if (tx["TransactionType"] == "EscrowFinish" && has_fulfillment)
            {
                double fulfillmentBytesSize = Math.Ceiling((double)tx["Fulfillment"].Length / 2);
                // 10 drops × (33 + (Fulfillment size in bytes / 16))
                double resp = (33 + (fulfillmentBytesSize / 16));
                bool product = BigInteger.TryParse(ScaleValue(netFeeDrops, 33 + (fulfillmentBytesSize / 16)), out var result);
                calculatedFee = BigInteger.Parse(Math.Ceiling(((decimal)result)).ToString());
            }

            // AccountDelete, AMMCreate Transaction
            else if (IsReserveFeeTxNeed(tx))
            {
                calculatedFee = await FetchReserveFee(client);
            }
            else if (tx["TransactionType"] == "Batch")
            {
                calculatedFee = baseFee * 3;
                if (!tx.TryGetValue("RawTransactions", out var rawTransactions) || rawTransactions == null)
                {
                    throw new ValidationException("Batch transaction must have RawTransactions field.");
                }

                // Поддержим JArray и любые коллекции
                IEnumerable<object> items = rawTransactions switch
                {
                    JArray ja => ja.ToObject<List<object>>()!,
                    IEnumerable<object> ie => ie,
                    _ => new List<object> { rawTransactions }
                };

                foreach (var inner in items)
                {
                    if (!TryGetInnerFieldsAsDict(inner, out var innerTx))
                        throw new ArgumentNullException(nameof(inner), "RawTransaction not found or invalid.");

                    if (IsReserveFeeTxNeed(innerTx))
                        calculatedFee += await FetchReserveFee(client);
                    else
                        calculatedFee += baseFee;
                }
            }


            /*
            * Multi-signed Transaction
            * 10 drops × (1 + Number of Signatures Provided)
            */
            else if (signersCount > 0)
            {
                signerFee = BigInteger.Add(baseFee, BigInteger.Parse(ScaleValue(netFeeDrops, 1 + signersCount)));
            }
            else
            {
                calculatedFee = baseFee;
            }

            calculatedFee += signerFee;

            var maxFeeDrops = XrpConversion.XrpToDrops(client.maxFeeXRP);
            var totalFee = tx["TransactionType"] == "AccountDelete" ? calculatedFee : BigInteger.Min(calculatedFee, BigInteger.Parse(maxFeeDrops));
            tx.TryAdd("Fee", Math.Ceiling(((decimal)totalFee)).ToString());
        }
        private static bool TryGetInnerFieldsAsDict(object item, out Dictionary<string, dynamic> dict)
        {
            dict = null!;

            // Приводим к JObject максимально рано
            JObject entry = item as JObject ?? JObject.FromObject(item);

            // Достаём RawTransaction
            var raw = entry["RawTransaction"] as JObject;
            if (raw == null) return false;

            // В словарь
            var tmp = raw.ToObject<Dictionary<string, dynamic>>();
            if (tmp == null) return false;

            dict = tmp;
            return true;
        }
        private static bool IsReserveFeeTxNeed(Dictionary<string, dynamic> tx)
        {
            return tx["TransactionType"] == "AccountDelete" || tx["TransactionType"] == "AMMCreate";
        }

        public static string ScaleValue(string value, double multiplier)
        {
            return (double.Parse(value)! * multiplier).ToString();
        }

        public static async Task SetLatestValidatedLedgerSequence(this IXrplClient client, Dictionary<string, dynamic> tx)
        {
            uint ledgerSequence = await client.GetLedgerIndex();
            tx.TryAdd("LastLedgerSequence", ledgerSequence + LEDGER_OFFSET);
        }

        public static async Task CheckAccountDeleteBlockers(this IXrplClient client, Dictionary<string, dynamic> tx)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Validated);
            AccountObjectsRequest request = new AccountObjectsRequest((string)tx["Account"])
            {
                LedgerIndex = index,
                DeletionBlockersOnly = true,
            };
            AccountObjects response = await client.AccountObjects(request);
            TaskCompletionSource task = new TaskCompletionSource();
            if (response.AccountObjectList.Count > 0)
            {
                task.TrySetException(new XrplException($"Account {(string)tx["Account"]} cannot be deleted; there are Escrows, PayChannels, RippleStates, or Checks associated with the account."));
            }
            task.TrySetResult();
        }
    }
}