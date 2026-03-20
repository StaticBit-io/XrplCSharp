using Newtonsoft.Json;

using System.Collections.Generic;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/gatewayBalances.ts

namespace Xrpl.Models.Methods;

/// <summary>
/// Expected response from a <see cref="GatewayBalancesRequest"/>.
/// </summary>
[JsonConverter(typeof(GatewayBalancesResponseConverter))]
public class GatewayBalancesResponse //todo rename to  GatewayBalancesResponse
{
    //todo change dynamic to class

    /// <summary>
    /// The address of the account that issued the balances.
    /// </summary>
    [JsonProperty("account")]
    public string Account { get; set; }

    /// <summary>
    /// Total amounts held that are issued by others.<br/>
    /// In the recommended  configuration, the issuing address should have none.
    /// </summary>
    [JsonProperty("assets")]
    public List<Currency> Assets { get; set; }

    /// <summary>
    /// Amounts issued to the hotwallet addresses from the request.<br/>
    /// The keys are  addresses and the values are arrays of currency amounts they hold.
    /// </summary>
    [JsonProperty("balances")]
    public List<Currency> Balances { get; init; }

    [JsonProperty("frozen_balances")]
    public List<Currency> FrozenBalances { get; init; }

    /// <summary>
    /// Total amounts issued to addresses not excluded, as a map of currencies  to the total value issued.
    /// </summary>
    [JsonProperty("obligations")]
    public List<Currency> Obligations { get; init; }

    [JsonProperty("ledger_hash")]
    public string LedgerHash { get; set; }

    [JsonProperty("ledger_index")]
    public uint? LedgerIndex { get; set; }

    [JsonProperty("validated")]
    public bool? Validated { get; set; }
}

/// <summary>
/// The gateway_balances command calculates the total balances issued by a given  account, optionally excluding amounts held by operational addresses.<br/>
/// Expects  a response in the form of a <see cref="GatewayBalancesResponse"/>.
/// </summary>
/// <code>
/// ```ts  const gatewayBalances: GatewayBalanceRequest = {
///     "id": "example_gateway_balances_1",
///     "command": "gateway_balances",
///     "account": "rMwjYedjc7qqtKYVLiAccJSmCwih4LnE2q",
///     "strict": true,
///     "hotwallet": ["rKm4uWpg9tfwbVSeATv4KxDe6mpE9yPkgJ","ra7JkEzrgeKHdzKgo4EUUVBnxggY4z37kt"],
///     "ledger_index": "validated"
/// }  ```.
/// </code>
public class GatewayBalancesRequest : BaseLedgerRequest
{
    public GatewayBalancesRequest(string account)
    {
        Account = account;
        Command = "gateway_balances";
    }

    /// <summary>
    /// The Address to check.<br/>
    /// This should be the issuing address.
    /// </summary>
    [JsonProperty("account")]
    public string Account { get; set; }

    /// <summary>
    /// If true, only accept an address or public key for the account parameter.<br/>
    /// Defaults to false.
    /// </summary>
    [JsonProperty("strict")]
    public bool? Strict { get; set; }

    /// <summary>
    /// An operational address to exclude from the balances issued, or an array of Such addresses.
    /// </summary>
    [JsonProperty("hotwallet")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public object HotWallet { get; set; }
}