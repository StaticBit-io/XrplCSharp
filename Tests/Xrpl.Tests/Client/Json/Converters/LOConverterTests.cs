using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class LOConverterTests
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    [TestMethod]
    public void Read_AccountRoot_ReturnsLOAccountRoot()
    {
        string json = @"{
            ""LedgerEntryType"": ""AccountRoot"",
            ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""Balance"": ""10000000000"",
            ""Flags"": 0,
            ""Sequence"": 1
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOAccountRoot));
        LOAccountRoot accountRoot = (LOAccountRoot)result;
        Assert.AreEqual("rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", accountRoot.Account);
        Assert.AreEqual(LedgerEntryType.AccountRoot, accountRoot.LedgerEntryType);
    }

    [TestMethod]
    public void Read_Offer_ReturnsLOOffer()
    {
        string json = @"{
            ""LedgerEntryType"": ""Offer"",
            ""Account"": ""rTest"",
            ""Flags"": 0,
            ""Sequence"": 100
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOOffer));
        Assert.AreEqual(LedgerEntryType.Offer, result.LedgerEntryType);
    }

    [TestMethod]
    public void Read_RippleState_ReturnsLORippleState()
    {
        string json = @"{
            ""LedgerEntryType"": ""RippleState"",
            ""Flags"": 0,
            ""LowLimit"": {""currency"": ""USD"", ""issuer"": ""rLow"", ""value"": ""0""},
            ""HighLimit"": {""currency"": ""USD"", ""issuer"": ""rHigh"", ""value"": ""100""}
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LORippleState));
        Assert.AreEqual(LedgerEntryType.RippleState, result.LedgerEntryType);
    }

    [TestMethod]
    public void Read_Escrow_ReturnsLOEscrow()
    {
        string json = @"{
            ""LedgerEntryType"": ""Escrow"",
            ""Account"": ""rTest"",
            ""Amount"": ""1000000"",
            ""Destination"": ""rDest""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOEscrow));
    }

    [TestMethod]
    public void Read_Check_ReturnsLOCheck()
    {
        string json = @"{
            ""LedgerEntryType"": ""Check"",
            ""Account"": ""rTest"",
            ""Destination"": ""rDest"",
            ""SendMax"": ""1000000""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOCheck));
    }

    [TestMethod]
    public void Read_AMM_ReturnsLOAmm()
    {
        string json = @"{
            ""LedgerEntryType"": ""Amm"",
            ""Account"": ""rAmmAccount""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOAmm));
    }

    [TestMethod]
    public void Read_DID_ReturnsLODID()
    {
        string json = @"{
            ""LedgerEntryType"": ""DID"",
            ""Account"": ""rTest""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LODID));
    }

    [TestMethod]
    public void Read_Oracle_ReturnsLOOracle()
    {
        string json = @"{
            ""LedgerEntryType"": ""Oracle"",
            ""Owner"": ""rTest""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOOracle));
    }

    [TestMethod]
    public void Read_Credential_ReturnsLOCredential()
    {
        string json = @"{
            ""LedgerEntryType"": ""Credential"",
            ""Subject"": ""rSubject"",
            ""Issuer"": ""rIssuer""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOCredential));
    }

    [TestMethod]
    public void Read_UnknownType_ReturnsBaseLedgerEntry()
    {
        string json = @"{
            ""LedgerEntryType"": ""FutureLedgerType"",
            ""Flags"": 0
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(BaseLedgerEntry));
        Assert.AreEqual(LedgerEntryType.Unknown, result.LedgerEntryType);
    }

    [TestMethod]
    public void Read_UnknownType_WithExtraFields_PreservesBaseProperties()
    {
        string json = @"{
            ""LedgerEntryType"": ""SomeNewLedgerObject"",
            ""index"": ""ABC123""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.AreEqual(LedgerEntryType.Unknown, result.LedgerEntryType);
        Assert.AreEqual("ABC123", result.Index);
    }

    [TestMethod]
    public void Read_DepositPreauth_ReturnsLODepositPreauth()
    {
        string json = @"{
            ""LedgerEntryType"": ""DepositPreauth"",
            ""Account"": ""rTest"",
            ""Authorize"": ""rAuth""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LODepositPreauth));
    }

    [TestMethod]
    public void Read_MPTokenIssuance_ReturnsLOMPTokenIssuance()
    {
        string json = @"{
            ""LedgerEntryType"": ""MPTokenIssuance"",
            ""Issuer"": ""rIssuer""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOMPTokenIssuance));
    }

    [TestMethod]
    public void Read_PermissionedDomain_ReturnsLOPermissionedDomain()
    {
        string json = @"{
            ""LedgerEntryType"": ""PermissionedDomain"",
            ""Owner"": ""rOwner""
        }";
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(LOPermissionedDomain));
    }
}
