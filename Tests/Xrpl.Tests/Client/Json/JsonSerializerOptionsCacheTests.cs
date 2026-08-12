using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace XrplTests.Client.Json;

/// <summary>
/// Polymorphic converters re-enter the serializer with their own converter removed. Building those
/// options per call allocated an options instance, copied the whole converter list and ran its own
/// structural-equality lookup in System.Text.Json's caching-context pool — for every value converted.
/// Type metadata was not rebuilt: since .NET 8 that pool shares one caching context between
/// structurally equal options instances. The cache does the work once per (source options, converter type).
/// </summary>
[TestClass]
public class TestUJsonSerializerOptionsCache
{
    [TestMethod]
    public void WithoutConverter_SameSourceAndConverter_ReturnsSameInstance()
    {
        JsonSerializerOptions source = XrplJsonOptions.Default;

        JsonSerializerOptions first = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(source);
        JsonSerializerOptions second = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(source);

        Assert.AreSame(first, second);
        Assert.AreNotSame(source, first);
    }

    [TestMethod]
    public void WithoutConverter_RemovesOnlyTheRequestedConverter()
    {
        JsonSerializerOptions derived = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(XrplJsonOptions.Default);

        foreach (JsonConverter converter in derived.Converters)
            Assert.IsNotInstanceOfType(converter, typeof(LOConverter));

        bool keptOthers = false;
        foreach (JsonConverter converter in derived.Converters)
        {
            if (converter is TransactionResponseConverter)
                keptOthers = true;
        }

        Assert.IsTrue(keptOthers, "Converters unrelated to the requested type must survive");
    }

    /// <summary>
    /// The source must be copied, never stripped in place: mutating it would remove LOConverter from the
    /// process-wide default options and every ledger object would silently degrade to a bare entry.
    /// </summary>
    [TestMethod]
    public void WithoutConverter_LeavesTheSourceOptionsIntact()
    {
        JsonSerializerOptionsCache.WithoutConverter<LOConverter>(XrplJsonOptions.Default);

        bool sourceStillHasIt = false;
        foreach (JsonConverter converter in XrplJsonOptions.Default.Converters)
        {
            if (converter is LOConverter)
                sourceStillHasIt = true;
        }

        Assert.IsTrue(sourceStillHasIt, "XrplJsonOptions.Default must keep its LOConverter");
    }

    [TestMethod]
    public void WithoutConverter_PreservesSourceSettings()
    {
        JsonSerializerOptions source = XrplJsonOptions.Default;
        JsonSerializerOptions derived = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(source);

        Assert.AreEqual(source.DefaultIgnoreCondition, derived.DefaultIgnoreCondition);
        Assert.AreEqual(source.PropertyNameCaseInsensitive, derived.PropertyNameCaseInsensitive);
        Assert.AreEqual(source.NumberHandling, derived.NumberHandling);
    }

    [TestMethod]
    public void WithoutConverter_DifferentConverterTypes_ReturnDifferentInstances()
    {
        JsonSerializerOptions source = XrplJsonOptions.Default;

        JsonSerializerOptions withoutLo = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(source);
        JsonSerializerOptions withoutTx = JsonSerializerOptionsCache.WithoutConverter<TransactionResponseConverter>(source);

        Assert.AreNotSame(withoutLo, withoutTx);
    }

    [TestMethod]
    public void WithoutConverter_DifferentSourceOptions_AreCachedSeparately()
    {
        JsonSerializerOptions otherSource = new JsonSerializerOptions();
        otherSource.Converters.Add(new LOConverter());

        JsonSerializerOptions fromDefault = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(XrplJsonOptions.Default);
        JsonSerializerOptions fromOther = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(otherSource);

        Assert.AreNotSame(fromDefault, fromOther);
        Assert.AreSame(fromOther, JsonSerializerOptionsCache.WithoutConverter<LOConverter>(otherSource));
    }

    /// <summary>
    /// The regression this cache needs guarded is a converter quietly going back to building its own copy
    /// per call. Options identity cannot detect that — System.Text.Json hands converters the options of the
    /// pooled caching context, so a per-call copy still shows up as one shared instance. What does detect it
    /// is the cache entry itself: the run below uses a private options instance no other test touches, so an
    /// entry for it can only have been created by a converter asking the cache during this deserialization.
    /// </summary>
    [TestMethod]
    public void Deserialize_GoesThroughTheCache()
    {
        JsonSerializerOptions privateOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        privateOptions.Converters.Add(new LOConverter());
        privateOptions.Converters.Add(new LedgerEntryTypeConverter());

        Assert.IsFalse(
            JsonSerializerOptionsCache.HasCachedEntry<LOConverter>(privateOptions),
            "Fresh options must start with no cache entry");

        string json = @"{""account"":""rTest"",""account_objects"":[
            {""LedgerEntryType"":""Offer"",""Account"":""rTest"",""Sequence"":1,""index"":""AA""},
            {""LedgerEntryType"":""Offer"",""Account"":""rTest"",""Sequence"":2,""index"":""BB""}]}";

        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(json, privateOptions);

        Assert.HasCount(2, response.AccountObjectList);
        Assert.IsInstanceOfType(response.AccountObjectList[0], typeof(LOOffer));
        Assert.IsTrue(
            JsonSerializerOptionsCache.HasCachedEntry<LOConverter>(privateOptions),
            "LOConverter must resolve its inner options through the cache, not build a copy per element");
    }
}
