using System.Text.Json;
using System.Text.Json.Serialization;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Enums;

namespace Xrpl.Client.Json
{
    /// <summary>
    /// Centralized System.Text.Json serializer options for the XRPL library.
    /// Registers all custom converters and configures default behavior.
    /// </summary>
    public static class XrplJsonOptions
    {
        /// <summary>
        /// Default options with all XRPL converters registered.
        /// Null properties are omitted from serialization output.
        /// Property names are case-insensitive during deserialization.
        /// </summary>
        public static JsonSerializerOptions Default { get; } = Create();

        private static JsonSerializerOptions Create()
        {
            // XrplJsonOptions does not use JsonStringEnumConverter intentionally — 
            // XRPL protocol enums are numeric flags and must be serialized as numbers.
            // String-based enums with [EnumMember] use EnumMemberValueConverter<T>,
            // enums without [EnumMember] use JsonStringEnumConverter — both applied at the enum/property level.
            
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            };

            // Dictionary<string, object> with primitive CLR types instead of JsonElement
            options.Converters.Add(new DictionaryObjectConverter());

            // Currency converters
            options.Converters.Add(new CurrencyConverter());
            options.Converters.Add(new IssuedCurrencyConverter());

            // DateTime converters
            options.Converters.Add(new RippleDateTimeConverter());
            options.Converters.Add(new UnixDateTimeConverter());
            options.Converters.Add(new FromStringDateTimeConverter());

            // UInt64 converters (registered globally but typically used via [JsonConverter] on properties)
            // options.Converters.Add(new UInt64StringJsonConverter());
            // options.Converters.Add(new UInt64HexJsonConverter());

            // Oracle converters (typically used via [JsonConverter] on properties)
            // options.Converters.Add(new AssetPriceConverter());
            // options.Converters.Add(new OracleCurrencyConverter());
            // options.Converters.Add(new OracleHexStringConverter());

            // Polymorphic converters
            options.Converters.Add(new LOConverter());
            options.Converters.Add(new TransactionResponseConverter());
            options.Converters.Add(new TransactionRequestConverter());
            options.Converters.Add(new TransactionOrHashConverter());
            options.Converters.Add(new LONFTokenConverter());

            // Ledger converters
            options.Converters.Add(new LedgerBinaryConverter());
            options.Converters.Add(new LedgerIndexConverter());

            // Metadata converter
            options.Converters.Add(new MetaBinaryConverter());

            // Feature converters
            options.Converters.Add(new ServerFeaturesConverter());
            options.Converters.Add(new GatewayBalancesResponseConverter());

            // Node converters (for transaction metadata AffectedNodes)
            options.Converters.Add(new ModifiedNodeConverter());
            options.Converters.Add(new CreatedNodeConverter());
            options.Converters.Add(new DeletedNodeConverter());

            // Misc converters (StringOrArrayConverter is used via [JsonConverter] attribute only)
            options.Converters.Add(new StreamTypeListConverter());

            // Enum converters with fallback to Unknown for unrecognized values
            options.Converters.Add(new TransactionTypeConverter());
            options.Converters.Add(new LedgerEntryTypeConverter());

            return options;
        }
    }
}
