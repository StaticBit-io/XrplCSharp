using System;
using Newtonsoft.Json;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/baseMethod.ts

namespace Xrpl.Models.Methods
{
    public class BaseRequest //todo rename to BaseRequest
    {
        public BaseRequest()
        {
            Id = null;
        }

        public BaseRequest(Guid id)
        {
            Id = id;
        }
        /// <summary>
        /// A unique value to identify this request.<br/>
        /// The response to this request uses the same id field.<br/>
        /// This way, even if responses arrive out of order, you know which request prompted which response.
        /// </summary>
        [JsonProperty("id")]
        public Guid? Id { get; set; }
        /** The name of the API method. */
        [JsonProperty("command")]
        public string Command { get; set; }

        /// <summary>
        /// The API version to use.<br/>
        /// If omitted, use version 1.
        /// </summary>
        [JsonProperty("api_version")]
        public uint? ApiVersion { get; set; } = 2;
    }
}
