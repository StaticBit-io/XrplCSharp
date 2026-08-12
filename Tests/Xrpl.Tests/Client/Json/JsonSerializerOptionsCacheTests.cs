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
/// options per call rebuilt System.Text.Json's per-instance type-metadata cache for every value
/// converted; the cache makes it once per (source options, converter type).
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
    /// The instance handed to converters during a real deserialization must be the cached one —
    /// this is what keeps the metadata cache warm across the elements of a collection.
    /// </summary>
    [TestMethod]
    public void Deserialize_ReusesTheCachedOptionsInstance()
    {
        string json = @"{""account"":""rTest"",""account_objects"":[
            {""LedgerEntryType"":""Offer"",""Account"":""rTest"",""Sequence"":1,""index"":""AA""},
            {""LedgerEntryType"":""Offer"",""Account"":""rTest"",""Sequence"":2,""index"":""BB""}]}";

        JsonSerializerOptions beforeRun = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(XrplJsonOptions.Default);

        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(json, XrplJsonOptions.Default);

        JsonSerializerOptions afterRun = JsonSerializerOptionsCache.WithoutConverter<LOConverter>(XrplJsonOptions.Default);

        Assert.HasCount(2, response.AccountObjectList);
        Assert.IsInstanceOfType(response.AccountObjectList[0], typeof(LOOffer));
        Assert.AreSame(beforeRun, afterRun);
    }
}
