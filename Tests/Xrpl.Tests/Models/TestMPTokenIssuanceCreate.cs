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
        public static Dictionary<string, dynamic> mpTokenIssuanceCreate;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenIssuanceCreate = new Dictionary<string, dynamic>
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
    }
}