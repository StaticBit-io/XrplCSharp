using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/sugar/balances.ts

namespace Xrpl.Sugar
{
    public class Balance
    {
        public string Value { get; set; }
        public string Currency { get; set; }
        public string Issuer { get; set; }
    }

    public class GetBalancesOptions
    {
        public string? LedgerHash { get; set; }
        public LedgerIndex? LedgerIndex { get; set; }
        public string Peer { get; set; }
        public int? Limit { get; set; }
    }

    public static class BalancesSugar
    {

        public static IEnumerable<Balance> FormatBalances(this IEnumerable<TrustLine> trustlines) =>
            trustlines.Select(Map);

        public static Balance Map(this TrustLine trustline) =>
                    new Balance()
                    {
                        Value = trustline.Balance,
                        Currency = trustline.Currency,
                        Issuer = trustline.Account,
                    };

        /// <summary>
        /// Get the XRP balance for an account.
        /// </summary>
        /// <param name="address">Address of the account to retrieve XRP balance.</param>
        /// <param name="lederIndex">Retrieve the account balances at a given ledgerIndex.</param>
        /// <param name="ledgerHash">Retrieve the account balances at the ledger with a given ledger_hash.</param>
        /// <param name="client">Client.</param>
        /// <returns/> The XRP balance of the account (as a string).
        public static async Task<string> GetXrpBalance(this IXrplClient client, string address, string ledgerHash = null, LedgerIndex lederIndex = null)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Validated);
            AccountInfoRequest xrpRequest = new AccountInfoRequest(address)
            {
                LedgerHash = ledgerHash,
                LedgerIndex = lederIndex ?? index,
                Strict = true
            };
            AccountInfo accountInfo = await client.AccountInfo(xrpRequest);
            return accountInfo.AccountData.Balance.ValueAsXrp.ToString();
        }

        /// <summary>
        /// Retrieves the available (free) XRP balance for the specified account address, accounting for network reserve
        /// requirements.
        /// </summary>
        /// <remarks>The free balance is calculated by subtracting the XRP Ledger's required account and
        /// owner reserves from the account's total balance. This value represents the amount of XRP that can be sent or
        /// withdrawn. The method queries the latest validated ledger by default unless a specific ledger hash or index
        /// is provided.</remarks>
        /// <param name="address">The XRP Ledger account address for which to retrieve the free balance. Must be a valid account address.</param>
        /// <param name="ledgerHash">The hash of the ledger to query. If null, the method uses the ledger specified by 'lederIndex' or the latest
        /// validated ledger.</param>
        /// <param name="lederIndex">The ledger index to query. If null, the latest validated ledger is used.</param>
        /// <param name="client">Client.</param>
        /// <returns>A decimal value representing the amount of XRP that is available for spending or transfer from the specified
        /// account, after subtracting the account and owner reserve requirements.</returns>
        public static async Task<decimal> GetXrpFreeBalance(this IXrplClient client, string address, string? ledgerHash = null, LedgerIndex? lederIndex = null)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Validated);
            AccountInfoRequest xrpRequest = new AccountInfoRequest(address)
            {
                LedgerHash = ledgerHash,
                LedgerIndex = lederIndex ?? index,
                Strict = true
            };
            AccountInfo accountInfo = await client.AccountInfo(xrpRequest);

            var serverInfo = await client.ServerState(new ServerStateRequest());
            var FlineReserveFee = serverInfo.State.ValidatedLedger.ReserveInc.ToString();
            var FaccReserveFee = serverInfo.State.ValidatedLedger.ReserveBase.ToString();
            var lineReserveFee = (decimal)new Currency()
            {
                Value = FlineReserveFee
            }.ValueAsXrp;
            var accReserveFee = (decimal)new Currency()
            {
                Value = FaccReserveFee
            }.ValueAsXrp;

            var numLines = accountInfo.AccountData.OwnerCount;
            var totalReserve = accReserveFee + (lineReserveFee * numLines);
            var freeBalance = (decimal)accountInfo.AccountData.Balance.ValueAsXrp - totalReserve;
            return freeBalance;
        }

        /// <param name="client">Client.</param>
        public static async Task<List<Balance>> GetBalances(this IXrplClient client, string address, GetBalancesOptions options = null)
        {
            var linesRequest = new AccountLinesRequest(address)
            {
                Command = "account_lines",
                LedgerIndex = options?.LedgerIndex ?? new LedgerIndex(LedgerIndexType.Validated),
                LedgerHash = options?.LedgerHash,
                Peer = options?.Peer,
                Limit = options?.Limit
            };

            var response = await client.AccountLines(linesRequest);
            var lines = response.TrustLines;
            while (response.Marker is not null && lines.Count > 0)
            {
                linesRequest.Marker = response.Marker;
                response = await client.AccountLines(linesRequest);
                if (response.TrustLines.Count > 0)
                    lines.AddRange(response.TrustLines);
                if (options?.Limit is not null && lines.Count >= options.Limit)
                    break;
            }
            var balances = lines.FormatBalances().ToList();

            if (options?.Peer == null)
            {
                var xrp_balance = await GetXrpBalance(client, address, options?.LedgerHash, options?.LedgerIndex);
                if (!string.IsNullOrWhiteSpace(xrp_balance))
                {
                    balances.Insert(0, new Balance { Currency = "XRP", Value = xrp_balance });
                }

            }

            return balances;
        }
    }
}