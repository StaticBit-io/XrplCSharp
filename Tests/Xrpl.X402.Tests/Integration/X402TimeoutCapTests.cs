using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;
using Xrpl.X402;
using XrplTests.Xrpl.ClientLib.Integration;

namespace Xrpl.X402.Tests.Integration;

[TestClass]
public class X402TimeoutCapTests
{
    [TestMethod]
    public async Task TestICapsLastLedgerSequenceByTimeout()
    {
        SetupIntegration runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        XrplWalletX402Signer signer = new(runner.client, runner.wallet);

        Payment Make() => new()
        {
            Account = runner.wallet.ClassicAddress,
            Destination = runner.wallet.ClassicAddress,
            Amount = new Currency { CurrencyCode = "XRP", Value = "1" }
        };

        string shortBlob = await signer.PrepareAndSignAsync(Make(), maxTimeoutSeconds: 8);
        string longBlob = await signer.PrepareAndSignAsync(Make(), maxTimeoutSeconds: 400);

        uint shortLls = ReadLls(shortBlob);
        uint longLls = ReadLls(longBlob);

        Assert.IsTrue(shortLls < longLls, $"expected short LLS < long LLS, got {shortLls} vs {longLls}");
    }

    private static uint ReadLls(string blob)
    {
        JsonElement tx = JsonDocument.Parse(XrplBinaryCodec.Decode(blob).ToString()).RootElement;
        return tx.GetProperty("LastLedgerSequence").GetUInt32();
    }
}
