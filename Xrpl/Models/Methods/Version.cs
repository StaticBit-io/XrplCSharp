// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/server-info-methods/version
// https://github.com/XRPLF/rippled/blob/develop/src/xrpld/rpc/handlers/server_info/Version.h
// https://github.com/XRPLF/rippled/blob/develop/include/xrpl/protocol/ApiVersion.h (setVersion)

using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Methods;

/// <summary>
/// The <c>version</c> method reports the range of API versions the server supports.
/// Returns a <see cref="VersionResponse"/>.
/// </summary>
public class VersionRequest : BaseRequest
{
    /// <summary>
    /// Creates the request; it takes no parameters.
    /// </summary>
    public VersionRequest() { Command = "version"; }
}

/// <summary>
/// Response expected from a <see cref="VersionRequest"/>.
/// </summary>
public class VersionResponse : BaseMethodResult
{
    /// <summary>
    /// The API versions the server supports.
    /// </summary>
    [JsonPropertyName("version")]
    public ApiVersionRange Version { get; set; }
}

/// <summary>
/// The range of API versions a server supports.
/// </summary>
/// <remarks>
/// The shape depends on the API version of the request (rippled <c>setVersion</c>). Asked with API
/// version 1, the node answers with the legacy strings <c>first</c>, <c>good</c> and <c>last</c>, all
/// <c>"1.0.0"</c>. Asked with version 2 or later, as the SDK asks by default, it answers with the numbers
/// <c>first</c> (the lowest supported version) and <c>last</c> (the highest, or the beta version when
/// the server enables the beta API), and no <c>good</c>. Both are read as strings.
/// </remarks>
public class ApiVersionRange
{
    /// <summary>
    /// The lowest API version the server supports.
    /// </summary>
    [JsonPropertyName("first")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string First { get; set; }

    /// <summary>
    /// The legacy "good" version, sent only to API version 1 requests.
    /// </summary>
    [JsonPropertyName("good")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string Good { get; set; }

    /// <summary>
    /// The highest API version the server supports.
    /// </summary>
    [JsonPropertyName("last")]
    [JsonConverter(typeof(NumberOrStringConverter))]
    public string Last { get; set; }
}
