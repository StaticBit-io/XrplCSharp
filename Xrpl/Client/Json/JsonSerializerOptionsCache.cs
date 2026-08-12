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
    /// Building those options inside Read/Write cost an allocation, a copy of the whole converter list and a
    /// structural-equality lookup in System.Text.Json's caching-context pool — once per converted value, so
    /// once per element of a collection. Type metadata itself was not rebuilt: since .NET 8 System.Text.Json
    /// shares a caching context between structurally equal options instances, which is what kept the per-call
    /// copy from being far worse than it was. That pool is capped (64 contexts); caching here removes the
    /// dependency on it as well.<br/>
    /// Measured on 200 <c>account_objects</c> pages of 200 entries: 456 ms / 47 MB allocated before,
    /// 217 ms / 29 MB after.<br/>
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

        /// <summary>
        /// Whether <paramref name="options"/> already has a cached entry for <typeparamref name="TConverter"/>.
        /// Exists so tests can assert that a converter went through the cache rather than building its own
        /// copy — the two are indistinguishable from the outside, because System.Text.Json hands converters
        /// the options of the pooled caching context rather than the instance they were called with.
        /// </summary>
        internal static bool HasCachedEntry<TConverter>(JsonSerializerOptions options)
            where TConverter : JsonConverter
        {
            return Cache.TryGetValue(options, out ConcurrentDictionary<Type, JsonSerializerOptions> byConverterType)
                && byConverterType.ContainsKey(typeof(TConverter));
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
