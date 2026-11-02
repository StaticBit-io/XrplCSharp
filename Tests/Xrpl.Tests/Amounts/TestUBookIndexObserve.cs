using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

[TestClass]
public class TestUBookIndexObserve
{
    private static readonly IssuedCurrency Cny = new IssuedCurrency { Currency = "CNY", Issuer = "rJ1adrpGS3xsnQMb9Cw54tWJVFPuSdZHK" };

    private static Meta MetaWith(string newFields) => JsonSerializer.Deserialize<Meta>(
        "{\"AffectedNodes\":[{\"CreatedNode\":{\"LedgerEntryType\":\"DirectoryNode\"," +
        "\"LedgerIndex\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E2BD6998872D5\"," +
        "\"NewFields\":" + newFields + "}}],\"TransactionIndex\":40,\"TransactionResult\":\"tesSUCCESS\"}",
        XrplJsonOptions.Default);

    [TestMethod]
    public void TestUObserve_BookWithAnXrpSide_IsAdded()
    {
        // NewFields of a mainnet OfferCreate (ledger 66093882): the XRP side's zero currency and
        // issuer are default values, which metadata leaves out.
        Meta meta = MetaWith(
            "{\"ExchangeRate\":\"4f1e2bd6998872d5\"," +
            "\"RootIndex\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E2BD6998872D5\"," +
            "\"TakerPaysCurrency\":\"000000000000000000000000434E590000000000\"," +
            "\"TakerPaysIssuer\":\"0360E3E0751BD9A566CD03FA6CAFC78118B82BA0\"}");
        BookIndex index = new BookIndex();

        index.Observe(meta);

        Assert.IsTrue(index.HasBookToXrp(Cny, null));
    }

    [TestMethod]
    public void TestUObserve_DirectoryWithBothSidesAbsent_IsIgnored()
    {
        Meta meta = MetaWith(
            "{\"ExchangeRate\":\"4f1e2bd6998872d5\"," +
            "\"RootIndex\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E2BD6998872D5\"}");
        BookIndex index = new BookIndex();

        index.Observe(meta);

        Assert.AreEqual(0, index.Count);
    }
}
