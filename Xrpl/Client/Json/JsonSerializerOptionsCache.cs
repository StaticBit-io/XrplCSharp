using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json
{
    /// <summary>
    /// Caches the derived <see cref="JsonSerializerOptions"/> that polymorphic converters build when they
    /// re-enter the serializer with their own converter removed to avoid infinite recursion.
    /// </summary>
    /// <remarks>
    /// System.Text.Json caches type metadata per options instance and the copy constructor does not carry
    /// that cache over, so building the derived options inside Read/Write rebuilt every contract for every
    /// value converted — once per element of a collection.<br/>
    /// Entries are keyed weakly by the source options instance, so caller-supplied options stay collectable,
    /// and by converter type within that instance.<br/>
    /// Caching the source is safe because System.Text.Json freezes an options instance on first use: by the
    /// time a converter runs, the options it was handed can no longer change.
    /// </remarks>
    internal static class JsonSerializerOptionsCache
    {
        private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<Type, JsonSerializerOptions>> Cache = new();

        /// <summary>
        /// Returns a copy of <paramref name="options"/> with every converter of type
        /// <typeparamref name="TConverter"/> removed. Repeated calls with the same source options and the
        /// same converter type return the same instance.
        /// </summary>
        /// <typeparam name="TConverter">Converter type to strip from the returned options.</typeparam>
        /// <param name="options">Source options, as handed to the converter.</param>
        public static JsonSerializerOptions WithoutConverter<TConverter>(JsonSerializerOptions options)
            where TConverter : JsonConverter
        {
            ConcurrentDictionary<Type, JsonSerializerOptions> byConverterType =
                Cache.GetValue(options, static _ => new ConcurrentDictionary<Type, JsonSerializerOptions>());

            return byConverterType.GetOrAdd(typeof(TConverter), static (_, source) => Build<TConverter>(source), options);
        }

        private static JsonSerializerOptions Build<TConverter>(JsonSerializerOptions source)
            where TConverter : JsonConverter
        {
            JsonSerializerOptions derived = new JsonSerializerOptions(source);
            for (int i = derived.Converters.Count - 1; i >= 0; i--)
            {
                if (derived.Converters[i] is TConverter)
                    derived.Converters.RemoveAt(i);
            }

            return derived;
        }
    }
}
