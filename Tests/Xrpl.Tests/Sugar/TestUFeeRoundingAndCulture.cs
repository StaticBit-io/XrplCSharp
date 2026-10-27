using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Sugar;
using Xrpl.Utils;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestUFeeRoundingAndCulture
{
    private const string BASE_FEE_XRP = "0.00001"; // 10 drops
    private const uint RESERVE_INC = 2000000;

    // A load factor rippled reports under moderate load; 10 drops times it is 10.3515625 drops.
    private const double FRACTIONAL_LOAD_FACTOR = 1.03515625;

    [TestMethod]
    public async Task TestUGetFeeXrp_FractionalLoadFactor_RoundsUpToWholeDrops()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC) { LoadFactor = FRACTIONAL_LOAD_FACTOR };
        client.feeCushion = 1.2;

        string fee = await client.GetFeeXrp();

        // 10 * 1.03515625 * 1.2 = 12.421875 drops, rounded up to 13.
        Assert.AreEqual("0.000013", fee);
        Assert.AreEqual("13", XrpConversion.XrpToDrops(fee));
    }

    [TestMethod]
    public async Task TestUGetFeeXrp_WholeDropFee_IsUnchanged()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC);
        client.feeCushion = 1.2;

        Assert.AreEqual("0.000012", await client.GetFeeXrp());
    }

    [TestMethod]
    public async Task TestUGetFeeXrp_SubDropCap_IsFlooredSoTheFeeStaysWithinIt()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC, maxFeeXRP: "0.0000115")
        {
            LoadFactor = FRACTIONAL_LOAD_FACTOR
        };
        client.feeCushion = 1.2;

        Assert.AreEqual("0.000011", await client.GetFeeXrp());
    }

    [TestMethod]
    public async Task TestUCalculateFee_SubDropCap_IsFlooredLikeGetFeeXrp()
    {
        // 10.3515625 drops rounds up to 11; the cap 10.5 drops floors to 10 on both paths.
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC, maxFeeXRP: "0.0000105")
        {
            LoadFactor = FRACTIONAL_LOAD_FACTOR
        };
        Dictionary<string, object> tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx);

        Assert.AreEqual("10", tx["Fee"]);
        Assert.AreEqual("0.00001", await client.GetFeeXrp());
    }

    [TestMethod]
    public async Task TestUAutofill_SubDropCap_IsFloored()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC, maxFeeXRP: "0.0000105")
        {
            LoadFactor = FRACTIONAL_LOAD_FACTOR,
            AccountRoot = new LOAccountRoot { Sequence = 7 }
        };
        Dictionary<string, object> tx = CreatePaymentTx();

        await AutofillSugar.Autofill(client, tx, null);

        Assert.AreEqual("10", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUCapBelowOneDrop_IsRefused()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC, maxFeeXRP: "0.0000005");

        await Assert.ThrowsExactlyAsync<ValidationException>(() => client.GetFeeXrp());
        await Assert.ThrowsExactlyAsync<ValidationException>(() => client.CalculateFeePerTransactionType(CreatePaymentTx()));
    }

    [TestMethod]
    public void TestUXrplClient_MaxFeeNotSet_DefaultsToTwoXrp()
    {
        using XrplClient noOptions = new XrplClient("ws://127.0.0.1:1");
        using XrplClient otherOptions = new XrplClient("ws://127.0.0.1:1", new XrplClient.ClientOptions { NetworkID = 1 });
        using XrplClient explicitCap = new XrplClient("ws://127.0.0.1:1", new XrplClient.ClientOptions { maxFeeXRP = "10" });

        Assert.AreEqual("2", noOptions.maxFeeXRP);
        Assert.AreEqual("2", otherOptions.maxFeeXRP);
        Assert.AreEqual("10", explicitCap.maxFeeXRP);
    }

    [TestMethod]
    public async Task TestUXrplClient_EmptyMaxFee_LeavesTheFeeUncapped()
    {
        // ChangeServer applies its options through the same SetSettings as the constructor.
        using XrplClient client = new XrplClient("ws://127.0.0.1:1", new XrplClient.ClientOptions { maxFeeXRP = "" });

        Assert.AreEqual("", client.maxFeeXRP);

        // The same empty cap on a fee source: 10 drops x 1,000,000 stays 10 XRP.
        FeeTestClient feeClient = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC, maxFeeXRP: client.maxFeeXRP)
        {
            LoadFactor = 1_000_000
        };
        Assert.AreEqual("10", await feeClient.GetFeeXrp());
    }

    [TestMethod]
    public async Task TestUDefaultCap_HighLoadFactor_HoldsTheFeeAtTwoXrp()
    {
        // 10 drops × 1,000,000 = 10 XRP before the cap.
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC, maxFeeXRP: XrplClient.DefaultMaxFeeXRP)
        {
            LoadFactor = 1_000_000
        };
        Dictionary<string, object> tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx);

        Assert.AreEqual("2", await client.GetFeeXrp());
        Assert.AreEqual("2000000", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUAutofill_TicketSequence_SetsSequenceZeroWithoutFetching()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC)
        {
            AccountRoot = new LOAccountRoot { Sequence = 7 }
        };
        Dictionary<string, object> tx = CreatePaymentTx();
        tx["TicketSequence"] = 5u;

        await AutofillSugar.Autofill(client, tx, null);

        Assert.AreEqual(0u, tx["Sequence"]);
        Assert.AreEqual(0, client.AccountInfoCalls);
    }

    [TestMethod]
    public async Task TestUAutofill_WithoutTicketSequence_FetchesSequence()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC)
        {
            AccountRoot = new LOAccountRoot { Sequence = 7 }
        };
        Dictionary<string, object> tx = CreatePaymentTx();

        await AutofillSugar.Autofill(client, tx, null);

        Assert.AreEqual(7u, tx["Sequence"]);
        Assert.AreEqual(1, client.AccountInfoCalls);
    }

    [TestMethod]
    public async Task TestUCalculateFee_FractionalLoadFactor_DoesNotThrow()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC) { LoadFactor = FRACTIONAL_LOAD_FACTOR };
        Dictionary<string, object> tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx);

        // 10.3515625 drops rounded up: never below base_fee * load_factor.
        Assert.AreEqual("11", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUAutofill_FillsSequenceFeeAndLastLedgerSequence()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC)
        {
            LoadFactor = FRACTIONAL_LOAD_FACTOR,
            AccountRoot = new LOAccountRoot { Sequence = 7 }
        };
        Dictionary<string, object> tx = CreatePaymentTx();

        await AutofillSugar.Autofill(client, tx, null);

        Assert.AreEqual(7u, tx["Sequence"]);
        Assert.AreEqual("11", tx["Fee"]);
        // FeeTestClient reports validated ledger 100; autofill adds LEDGER_OFFSET (20).
        Assert.AreEqual(120u, tx["LastLedgerSequence"]);
    }

    [TestMethod]
    public async Task TestUAutofill_ZeroLastLedgerSequence_IsRemovedAndNotFilled()
    {
        FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC)
        {
            AccountRoot = new LOAccountRoot { Sequence = 7 }
        };
        Dictionary<string, object> tx = CreatePaymentTx();
        tx["LastLedgerSequence"] = 0u;

        await AutofillSugar.Autofill(client, tx, null);

        Assert.IsFalse(tx.ContainsKey("LastLedgerSequence"));
        Assert.AreEqual("10", tx["Fee"]);
    }

    [TestMethod]
    public void TestUXrpToDrops_SevenFractionalDigits_IsRejected()
    {
        Assert.ThrowsExactly<ValidationException>(() => XrpConversion.XrpToDrops("1.0000005"));
        Assert.ThrowsExactly<ValidationException>(() => XrpConversion.XrpToDrops("0.0000001"));
        Assert.ThrowsExactly<ValidationException>(() => XrpConversion.XrpToDrops(0.0000001));
    }

    [TestMethod]
    public void TestUXrpToDrops_SixFractionalDigits_IsExact()
    {
        Assert.AreEqual("1000005", XrpConversion.XrpToDrops("1.000005"));
        Assert.AreEqual("1000005", XrpConversion.XrpToDrops("1.0000050"));
    }

    [TestMethod]
    public async Task TestUConversionsAndBalance_UnderRuRuCulture_UseInvariantFormat()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // The test project runs with InvariantGlobalization, which refuses GetCultureInfo("ru-RU");
            // a clone with ru-RU's number separators reproduces what the conversions are exposed to.
            CultureInfo ruRu = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            ruRu.NumberFormat.NumberDecimalSeparator = ",";
            ruRu.NumberFormat.NumberGroupSeparator = " ";
            CultureInfo.CurrentCulture = ruRu;
            CultureInfo.CurrentUICulture = ruRu;

            Assert.AreEqual("1500000", XrpConversion.XrpToDrops(1.5));
            Assert.AreEqual("1.5", XrpConversion.DropsToXrp("1500000"));
            Assert.AreEqual("-1.5", XrpConversion.DropsToXrp("-1500000"));

            FeeTestClient client = new FeeTestClient(BASE_FEE_XRP, RESERVE_INC, maxFeeXRP: "0.000011")
            {
                LoadFactor = FRACTIONAL_LOAD_FACTOR,
                AccountRoot = new LOAccountRoot { Balance = new Currency { Value = "1500000" } }
            };
            client.feeCushion = 1.2;

            // Static call: FeeTestClient declares its own GetXrpBalance, which would win over the sugar.
            Assert.AreEqual("1.5", await BalancesSugar.GetXrpBalance(client, "rTestAccount"));
            // maxFeeXRP "0.000011" is parsed invariantly; under ru-RU decimal.Parse would reject it.
            Assert.AreEqual("0.000011", await client.GetFeeXrp());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static Dictionary<string, object> CreatePaymentTx() => new()
    {
        ["TransactionType"] = "Payment",
        ["Account"] = "rTestAccount",
        ["Destination"] = "rDestination",
        ["Amount"] = "1000000"
    };
}
