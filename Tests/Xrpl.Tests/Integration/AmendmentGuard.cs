using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Methods;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Checks amendment activation on the test node so integration tests can be
/// marked inconclusive instead of failing when the node lacks the amendment.
/// </summary>
public static class AmendmentGuard
{
    /// <summary>Well-known index of the Amendments ledger object.</summary>
    public const string AmendmentsLedgerIndex = "7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4";

    /// <summary>Amendment id of BatchV1_1 (sha512half of the name).</summary>
    public const string BatchV11 = "9F287AED3CDB50A7BD1ACEC24296A30C9B5230CCD136219317AC790E3B884377";

    /// <summary>Amendment id of PermissionDelegationV1_1 (sha512half of the name).</summary>
    public const string PermissionDelegationV11 = "0F48FF561C709540328F31F1C97FD512ACC8B4E42138A161CB0E21ECA292540B";

    public static async Task<bool> IsEnabledAsync(IXrplClient client, string amendmentId)
    {
        try
        {
            LedgerEntryRequest request = new LedgerEntryRequest { Index = AmendmentsLedgerIndex };
            JsonNode node = await client.GRequest<JsonNode, LedgerEntryRequest>(request);
            JsonArray amendments = node?["node"]?["Amendments"]?.AsArray();
            if (amendments == null)
                return false;
            foreach (JsonNode amendment in amendments)
            {
                if (string.Equals(amendment?.GetValue<string>(), amendmentId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
