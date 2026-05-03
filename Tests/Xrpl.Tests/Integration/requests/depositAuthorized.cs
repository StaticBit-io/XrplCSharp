

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/depositAuthorized.ts

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    public class TestIDepositAuthorized
    {
        public TestContext TestContext { get; set; }
        public static SetupIntegration runner;

        [ClassInitialize]
        public static async Task MyClassInitializeAsync(TestContext testContext)
        {
            runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        }

        /// <summary>
        /// Verifies the <c>deposit_authorized</c> request between two arbitrary accounts
        /// (no Deposit Authorization enabled on destination — should always return true).
        /// </summary>
        [TestMethod]
        public async Task TestRequestMethod()
        {
            XrplWallet wallet2 = await Utils.GenerateFundedWallet(runner.client);

            DepositAuthorizedRequest request = new DepositAuthorizedRequest
            {
                SourceAccount = runner.wallet.ClassicAddress,
                DestinationAccount = wallet2.ClassicAddress,
            };

            DepositAuthorized response = await runner.client.DepositAuthorized(request);

            Assert.IsNotNull(response);
            Assert.AreEqual(runner.wallet.ClassicAddress, response.SourceAccount);
            Assert.AreEqual(wallet2.ClassicAddress, response.DestinationAccount);
            Assert.IsTrue(response.IsDepositAuthorized);
        }

        /// <summary>
        /// Verifies <c>deposit_authorized</c> with the XLS-70 <c>credentials</c> array.
        /// Creates and accepts a real credential, then references it by its on-ledger object ID.
        /// </summary>
        [TestMethod]
        public async Task TestRequestMethod_WithCredentials()
        {
            XrplWallet walletSubject = await Utils.GenerateFundedWallet(runner.client);

            string credTypeHex = ToHex($"deposit_auth_test_{Guid.NewGuid():N}");

            CredentialCreate createTx = new CredentialCreate
            {
                Account = runner.wallet.ClassicAddress,
                Subject = walletSubject.ClassicAddress,
                CredentialType = credTypeHex,
            };
            Dictionary<string, dynamic> createJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(createTx.ToJson());
            await Utils.TestTransaction(runner.client, createJson, runner.wallet);

            CredentialAccept acceptTx = new CredentialAccept
            {
                Account = walletSubject.ClassicAddress,
                Issuer = runner.wallet.ClassicAddress,
                CredentialType = credTypeHex,
            };
            Dictionary<string, dynamic> acceptJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(acceptTx.ToJson());
            await Utils.TestTransaction(runner.client, acceptJson, walletSubject);

            string credentialId = Hashes.HashCredential(
                walletSubject.ClassicAddress,
                runner.wallet.ClassicAddress,
                credTypeHex);

            DepositAuthorizedRequest request = new DepositAuthorizedRequest
            {
                SourceAccount = walletSubject.ClassicAddress,
                DestinationAccount = runner.wallet.ClassicAddress,
                Credentials = new List<string> { credentialId },
            };

            DepositAuthorized response = await runner.client.DepositAuthorized(request);

            Assert.IsNotNull(response);
            Assert.AreEqual(walletSubject.ClassicAddress, response.SourceAccount);
            Assert.AreEqual(runner.wallet.ClassicAddress, response.DestinationAccount);
            Assert.IsTrue(response.IsDepositAuthorized);
        }

        private static string ToHex(string text)
        {
            return BitConverter.ToString(Encoding.UTF8.GetBytes(text)).Replace("-", "");
        }
    }
}
