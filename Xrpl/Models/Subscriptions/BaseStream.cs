using System.Text.Json.Serialization;

namespace Xrpl.Models.Subscriptions
{
    public class BaseStream
    {
        /// <summary>
        /// consensusPhase indicates this is from the consensus stream<br/>
        /// consensusPhase - type
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ResponseStreamType Type { get; set; }
    }
}