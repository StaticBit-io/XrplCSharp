using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text;

using Xrpl.Client;
using Xrpl.Client.Json;
using Xrpl.Models.Ledger;

namespace XrplTests.Client;

/// <summary>
/// The envelope a caller gets back: the typed projection and, beside it, the bytes the node sent.
/// The point of the pair is that the projection cannot be mistaken for the source — re-serializing
/// it drops members the model lacks and invents defaults for non-nullable CLR properties.
/// </summary>
[TestClass]
public class TestUXrplResponse
{
    [TestMethod]
    public void TestUCarriesResultAndRawSideBySide()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"ledger_index\":9,\"marker\":\"AABB\"}}");
        RawJson raw = new RawJson(frame, 10, frame.Length - 11);
        LOLedgerData typed = raw.Deserialize<LOLedgerData>();

        XrplResponse<LOLedgerData> response = new XrplResponse<LOLedgerData>(typed, raw, 2, null, false);

        Assert.AreSame(typed, response.Result);
        Assert.AreEqual("{\"ledger_index\":9,\"marker\":\"AABB\"}", response.Raw.ToString());
        Assert.AreEqual(2u, response.ApiVersion);
    }

    [TestMethod]
    public void TestUWarningsAreNeverNull()
    {
        XrplResponse<LOLedgerData> response = new XrplResponse<LOLedgerData>(null, default, null, null, false);

        Assert.IsNotNull(response.Warnings);
        Assert.AreEqual(0, response.Warnings.Count);
    }
}
