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

    /// <summary>Amendment id of Sponsor / XLS-68 (sha512half of the name).</summary>
    public const string Sponsor = "BE1F90581635DBCEBFC4678C4B54FEDDC1A17B50FD02CFE765A4132A342126AC";

    /// <summary>Amendment id of ConfidentialTransfer (sha512half of the name).</summary>
    public const string ConfidentialTransfer = "2110E4A19966E2EF517C0A8C56A5F35099D7665B0BB89D7B126B30D50B86AAD5";

    /// <summary>Amendment id of PriceOracle / XLS-47 (sha512half of the name).</summary>
    public const string PriceOracle = "96FD2F293A519AE1DB6F8BED23E4AD9119342DA7CB6BAFD00953D16C54205D8B";

    /// <summary>Amendment id of PermissionedDomains / XLS-80 (sha512half of the name).</summary>
    public const string PermissionedDomains = "A730EB18A9D4BB52502C898589558B4CCEB4BE10044500EE5581137A2E80E849";

    public static async Task<bool> IsEnabledAsync(IXrplClient client, string amendmentId)
    {
        try
        {
            LedgerEntryRequest request = new LedgerEntryRequest { Index = AmendmentsLedgerIndex };
            JsonNode node = await client.GRequest<JsonNode, LedgerEntryRequest>(request);
            JsonArray amendments = node?["node"]?["Amendments"]?.AsArray();
            if (amendments != null)
            {
                foreach (JsonNode amendment in amendments)
                {
                    if (string.Equals(amendment?.GetValue<string>(), amendmentId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            // Fall through to the admin feature check below.
        }

        // Standalone --start force-enables the [features] amendments without
        // recording them in the Amendments ledger object (observed on 3.2.0:
        // the object lists 3 entries while dozens are active). The admin
        // `feature` command reports the node's actual view.
        return await IsEnabledViaFeatureCommandAsync(client, amendmentId);
    }

    private static async Task<bool> IsEnabledViaFeatureCommandAsync(IXrplClient client, string amendmentId)
    {
        try
        {
            FeatureRequest request = new FeatureRequest { Feature = amendmentId };
            JsonNode node = await client.GRequest<JsonNode, FeatureRequest>(request);
            JsonNode entry = node?[amendmentId];
            return entry?["enabled"]?.GetValue<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    private class FeatureRequest : BaseRequest
    {
        public FeatureRequest() => Command = "feature";

        [System.Text.Json.Serialization.JsonPropertyName("feature")]
        public string Feature { get; set; }
    }
}
