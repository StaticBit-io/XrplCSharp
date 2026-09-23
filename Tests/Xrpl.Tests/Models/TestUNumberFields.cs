using System.Collections.Generic;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

namespace Xrpl.Tests.Models.Tests;

/// <summary>
/// The XRPL Number fields of the vault and lending models carry <see cref="XrplNumber"/>: read from
/// the text rippled writes, written back in the same form, and encoded to the bytes rippled signs.
/// </summary>
[TestClass]
public class TestUNumberFields
{
    private const string LoanBrokerId = "0000000000000000000000000000000000000000000000000000000000000001";

    [TestMethod]
    public void TestULoanSet_PrincipalRequested_EncodesToRippledBytes()
    {
        LoanSet loanSet = new LoanSet
        {
            Account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            LoanBrokerID = LoanBrokerId,
            Counterparty = "rnUy2SHTrB9DubsPmkJZUXTf5FcNDGrYEA",
            PrincipalRequested = XrplNumber.Parse("10000000000000"),
            LoanServiceFee = 5,
        };

        StringAssert.Contains(loanSet.ToJson(), "\"PrincipalRequested\":\"1e13\"");
        StringAssert.Contains(loanSet.ToJson(), "\"LoanServiceFee\":\"5\"");

        Dictionary<string, object> tx = loanSet.ToDictionary();
        string blob = XrplBinaryCodec.Encode(tx);

        // rippled's tx_blob for PrincipalRequested = 10000000000000: mantissa 10^18, exponent -5.
        StringAssert.Contains(blob, "0DE0B6B3A7640000FFFFFFFB");
    }

    [TestMethod]
    public void TestULoanSet_UnsetNumberFields_AreOmitted()
    {
        LoanSet loanSet = new LoanSet
        {
            Account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            LoanBrokerID = LoanBrokerId,
            PrincipalRequested = 1000,
        };

        string json = loanSet.ToJson();
        Assert.IsFalse(json.Contains("LoanOriginationFee"), json);
        Assert.IsFalse(json.Contains("ClosePaymentFee"), json);
    }

    [TestMethod]
    public void TestULOLoan_ReadsNumberFieldsAsRippledWritesThem()
    {
        const string json = @"{
            ""LedgerEntryType"": ""Loan"",
            ""Borrower"": ""rnUy2SHTrB9DubsPmkJZUXTf5FcNDGrYEA"",
            ""LoanBrokerID"": """ + LoanBrokerId + @""",
            ""PrincipalOutstanding"": ""1e13"",
            ""TotalValueOutstanding"": ""10000000833.3333333"",
            ""PeriodicPayment"": ""833.3333333333333333"",
            ""ManagementFeeOutstanding"": ""0"",
            ""LoanServiceFee"": ""2e-11"",
            ""index"": ""0000000000000000000000000000000000000000000000000000000000000002""
        }";

        LOLoan loan = JsonSerializer.Deserialize<LOLoan>(json, XrplJsonOptions.Default);

        Assert.AreEqual(XrplNumber.Parse("10000000000000"), loan.PrincipalOutstanding);
        Assert.AreEqual(10000000833.3333333m, (decimal)loan.TotalValueOutstanding.Value);
        Assert.AreEqual("833.3333333333333333", loan.PeriodicPayment.ToString());
        Assert.AreEqual(XrplNumber.Zero, loan.ManagementFeeOutstanding);
        Assert.AreEqual(XrplNumber.Parse("0.00000000002"), loan.LoanServiceFee);
        Assert.IsNull(loan.LatePaymentFee);
    }

    [TestMethod]
    public void TestULOVault_ReadsNumberFieldsAsRippledWritesThem()
    {
        const string json = @"{
            ""LedgerEntryType"": ""Vault"",
            ""Owner"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""AssetsTotal"": ""5e10"",
            ""AssetsAvailable"": ""49999999999"",
            ""AssetsMaximum"": ""9223372036854775807"",
            ""LossUnrealized"": ""0"",
            ""index"": ""0000000000000000000000000000000000000000000000000000000000000003""
        }";

        LOVault vault = JsonSerializer.Deserialize<LOVault>(json, XrplJsonOptions.Default);

        Assert.AreEqual(XrplNumber.Parse("50000000000"), vault.AssetsTotal);
        Assert.IsTrue(vault.AssetsAvailable < vault.AssetsTotal);
        Assert.AreEqual((XrplNumber)long.MaxValue, vault.AssetsMaximum);
        Assert.IsTrue(vault.LossUnrealized.Value.IsZero);
    }
}
