using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUMPTokenIssuanceCreate
    {
        public static Dictionary<string, object> mpTokenIssuanceCreate;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenIssuanceCreate = new Dictionary<string, object>
            {
                {"TransactionType", "MPTokenIssuanceCreate"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {
            await Validation.Validate(mpTokenIssuanceCreate);
        }

        [TestMethod]
        public async Task TestVerifyWithAssetScale()
        {
            mpTokenIssuanceCreate["AssetScale"] = (byte)2;
            await Validation.Validate(mpTokenIssuanceCreate);
            mpTokenIssuanceCreate.Remove("AssetScale");
        }

        [TestMethod]
        public async Task TestVerifyWithTransferFee()
        {
            mpTokenIssuanceCreate["TransferFee"] = (ushort)1000;
            await Validation.Validate(mpTokenIssuanceCreate);
            mpTokenIssuanceCreate.Remove("TransferFee");
        }

        [TestMethod]
        public async Task TestVerifyWithMaximumAmount()
        {
            mpTokenIssuanceCreate["MaximumAmount"] = "9223372036854775807";
            await Validation.Validate(mpTokenIssuanceCreate);
            mpTokenIssuanceCreate.Remove("MaximumAmount");
        }

        [TestMethod]
        public async Task TestVerifyWithMPTokenMetadata()
        {
            mpTokenIssuanceCreate["MPTokenMetadata"] = "48656C6C6F";
            await Validation.Validate(mpTokenIssuanceCreate);
            mpTokenIssuanceCreate.Remove("MPTokenMetadata");
        }

        [TestMethod]
        public async Task TestThrowsWithTransferFeeOutOfRange()
        {
            mpTokenIssuanceCreate["TransferFee"] = (ushort)50001;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceCreate),
                "MPTokenIssuanceCreate: TransferFee must be between 0 and 50000");
            mpTokenIssuanceCreate.Remove("TransferFee");
        }

        [TestMethod]
        public async Task TestThrowsWithAssetScaleOutOfRange()
        {
            mpTokenIssuanceCreate["AssetScale"] = (byte)11;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceCreate),
                "MPTokenIssuanceCreate: AssetScale must be between 0 and 10");
            mpTokenIssuanceCreate.Remove("AssetScale");
        }

        private const string ValidDomainId = "77D6234D074E505024D39C04C3F262997B773719AB29ACFA83119E4210328776";

        [TestMethod]
        public async Task TestVerifyWithDomainIdAndRequireAuth()
        {
            // rippled: DomainID implies a non-public issuance - tfMPTRequireAuth required
            mpTokenIssuanceCreate["DomainID"] = ValidDomainId;
            mpTokenIssuanceCreate["Flags"] = (uint)MPTokenIssuanceCreateFlags.tfMPTRequireAuth;
            await Validation.Validate(mpTokenIssuanceCreate);
            mpTokenIssuanceCreate.Remove("DomainID");
            mpTokenIssuanceCreate.Remove("Flags");
        }

        [TestMethod]
        public async Task TestThrowsWithDomainIdWithoutRequireAuth()
        {
            mpTokenIssuanceCreate["DomainID"] = ValidDomainId;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceCreate),
                "MPTokenIssuanceCreate: DomainID requires the tfMPTRequireAuth flag");
            mpTokenIssuanceCreate.Remove("DomainID");
        }

        [TestMethod]
        public async Task TestThrowsWithMalformedDomainId()
        {
            mpTokenIssuanceCreate["DomainID"] = "NOT-A-HASH";
            mpTokenIssuanceCreate["Flags"] = (uint)MPTokenIssuanceCreateFlags.tfMPTRequireAuth;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceCreate),
                "MPTokenIssuanceCreate: DomainID must be a 64-character hexadecimal string");
            mpTokenIssuanceCreate.Remove("DomainID");
            mpTokenIssuanceCreate.Remove("Flags");
        }

        [TestMethod]
        public async Task TestThrowsWithZeroDomainId()
        {
            mpTokenIssuanceCreate["DomainID"] = new string('0', 64);
            mpTokenIssuanceCreate["Flags"] = (uint)MPTokenIssuanceCreateFlags.tfMPTRequireAuth;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceCreate),
                "MPTokenIssuanceCreate: DomainID must not be zero");
            mpTokenIssuanceCreate.Remove("DomainID");
            mpTokenIssuanceCreate.Remove("Flags");
        }
    }
}