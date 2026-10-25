using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models.Transactions;

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// Reads and writes an STArray of inner objects as a plain list of those objects.
    /// </summary>
    /// <remarks>
    /// rippled renders every member of an STArray wrapped in an object named after the inner
    /// object's field (<c>[{"Majority": {...}}, ...]</c>), so binding the array straight to a list
    /// of the inner type yields elements with every field left at its default. This converter
    /// unwraps on read and wraps again on write. An element that is not wrapped is read as the
    /// inner object itself, so a list built by hand in the flat shape still binds.
    /// </remarks>
    /// <typeparam name="T">The inner object type.</typeparam>
    public abstract class InnerObjectListConverter<T> : JsonConverter<List<T>>
    {
        private readonly string _wrapperName;

        /// <param name="wrapperName">The field name rippled wraps each element in.</param>
        protected InnerObjectListConverter(string wrapperName)
        {
            _wrapperName = wrapperName ?? throw new ArgumentNullException(nameof(wrapperName));
        }

        /// <inheritdoc />
        /// <exception cref="JsonException">The value is neither null nor an array.</exception>
        public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected an array of {_wrapperName} objects, got {reader.TokenType}.");

            List<T> items = new List<T>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                JsonElement element = document.RootElement;
                JsonElement inner = element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(_wrapperName, out JsonElement wrapped)
                    && wrapped.ValueKind == JsonValueKind.Object
                        ? wrapped
                        : element;
                items.Add(inner.Deserialize<T>(options));
            }

            return items;
        }

        /// <inheritdoc />
        /// <exception cref="JsonException">The list holds a null element, which no STArray can carry.</exception>
        public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T item in value)
            {
                if (item is null)
                    throw new JsonException($"A {_wrapperName} list cannot hold a null element.");

                writer.WriteStartObject();
                writer.WritePropertyName(_wrapperName);
                JsonSerializer.Serialize(writer, item, options);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
    }

    /// <summary><see cref="LOAmendments.Majorities"/>: <c>[{"Majority": {...}}]</c>.</summary>
    public sealed class MajorityListConverter : InnerObjectListConverter<Majority>
    {
        /// <summary>Creates the converter.</summary>
        public MajorityListConverter() : base("Majority") { }
    }

    /// <summary><see cref="LONegativeUNL.DisabledValidators"/>: <c>[{"DisabledValidator": {...}}]</c>.</summary>
    public sealed class DisabledValidatorListConverter : InnerObjectListConverter<DisabledValidator>
    {
        /// <summary>Creates the converter.</summary>
        public DisabledValidatorListConverter() : base("DisabledValidator") { }
    }

    /// <summary><see cref="LOAmm.VoteSlots"/>: <c>[{"VoteEntry": {...}}]</c>.</summary>
    public sealed class AMMVoteEntryListConverter : InnerObjectListConverter<AMMVoteEntry>
    {
        /// <summary>Creates the converter.</summary>
        public AMMVoteEntryListConverter() : base("VoteEntry") { }
    }

    /// <summary><see cref="AMMAuctionSlot.AuthAccounts"/>: <c>[{"AuthAccount": {"Account": ...}}]</c>.</summary>
    public sealed class AuthAccountEntryListConverter : InnerObjectListConverter<AuthAccountEntry>
    {
        /// <summary>Creates the converter.</summary>
        public AuthAccountEntryListConverter() : base("AuthAccount") { }
    }
}
