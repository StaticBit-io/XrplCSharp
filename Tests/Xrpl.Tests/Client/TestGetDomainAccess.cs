using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Exceptions;
using Xrpl.Sugar;

namespace Xrpl.Tests.ClientLib
{
    [TestClass]
    public class TestUGetDomainAccess
    {
        public static SetupUnitClient runner;

        [TestInitialize]
        public async Task MyTestInitializeAsync()
        {
            runner = await new SetupUnitClient().SetupClient();
        }

        [TestCleanup]
        public async Task MyTestCleanupAsync()
        {
            await runner.client.Disconnect();
        }

        [TestMethod]
        public async Task TestGetDomainAccess_MissingCloseTime_Throws()
        {
            // A validated ledger header without close_time must fail the check
            // explicitly instead of silently falling back to wall-clock time.
            string ledgerJson = "{\"id\":0,\"status\":\"success\",\"type\":\"response\",\"result\":{\"ledger\":{\"account_hash\":\"EC028EC32896D537ECCA18D18BEBE6AE99709FEFF9EF72DBD3A7819E918D8B96\",\"parent_close_time\":464908900,\"close_time_resolution\":10,\"closed\":true,\"close_flags\":0,\"ledger_hash\":\"0F7ED9F40742D8A513AE86029462B7A6768325583DF8EE21B7EC663019DD6A0F\",\"ledger_index\":\"9038214\",\"parent_hash\":\"4BB9CBE44C39DC67A1BE849C7467FE1A6D1F73949EA163C38A0121A15E04FFDE\",\"total_coins\":\"99999973964317514\",\"transaction_hash\":\"ECB730839EB55B1B114D5D1AD2CD9A932C35BA9AB6D3A8C2F08935EAC2BAC239\",\"transactions\":[]},\"ledger_hash\":\"1723099E269C77C4BDE86C83FA6415D71CF20AA5CB4A94E5C388ED97123FB55B\",\"ledger_index\":9038214,\"validated\":true}}";
            runner.mockedRippled.AddResponse("ledger", JsonSerializer.Deserialize<Dictionary<string, object>>(ledgerJson));

            // Guard response in case the implementation reaches ledger_entry anyway.
            string entryErrorJson = "{\"id\":0,\"status\":\"error\",\"type\":\"response\",\"error\":\"entryNotFound\",\"error_code\":92,\"error_message\":\"Entry not found.\",\"request\":{\"command\":\"ledger_entry\"}}";
            runner.mockedRippled.AddResponse("ledger_entry", JsonSerializer.Deserialize<Dictionary<string, object>>(entryErrorJson));

            RippleException exception = await Assert.ThrowsExactlyAsync<RippleException>(() =>
                runner.client.GetDomainAccess(
                    "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn",
                    "A730EB18A9D4BB52502C898589558B4CCEB4BE10044500EE5581137A2E80E849"));
            StringAssert.Contains(exception.Message, "did not include a close time");
        }

        private const string Owner = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";
        private const string DomainId = "A730EB18A9D4BB52502C898589558B4CCEB4BE10044500EE5581137A2E80E849";

        private static void MockLedgerAndDomain()
        {
            string ledgerJson = "{\"id\":0,\"status\":\"success\",\"type\":\"response\",\"result\":{\"ledger\":{\"account_hash\":\"EC028EC32896D537ECCA18D18BEBE6AE99709FEFF9EF72DBD3A7819E918D8B96\",\"close_time\":464908910,\"parent_close_time\":464908900,\"close_time_resolution\":10,\"closed\":true,\"close_flags\":0,\"ledger_hash\":\"0F7ED9F40742D8A513AE86029462B7A6768325583DF8EE21B7EC663019DD6A0F\",\"ledger_index\":\"9038214\",\"parent_hash\":\"4BB9CBE44C39DC67A1BE849C7467FE1A6D1F73949EA163C38A0121A15E04FFDE\",\"total_coins\":\"99999973964317514\",\"transaction_hash\":\"ECB730839EB55B1B114D5D1AD2CD9A932C35BA9AB6D3A8C2F08935EAC2BAC239\",\"transactions\":[]},\"ledger_hash\":\"1723099E269C77C4BDE86C83FA6415D71CF20AA5CB4A94E5C388ED97123FB55B\",\"ledger_index\":9038214,\"validated\":true}}";
            runner.mockedRippled.AddResponse("ledger", JsonSerializer.Deserialize<Dictionary<string, object>>(ledgerJson));

            // Every ledger_entry answers with the domain, so a credential lookup finds no Credential.
            string domainJson = "{\"id\":0,\"status\":\"success\",\"type\":\"response\",\"result\":{\"index\":\"" + DomainId + "\",\"ledger_index\":9038214,\"validated\":true,\"node\":{\"LedgerEntryType\":\"PermissionedDomain\",\"Owner\":\"" + Owner + "\",\"Flags\":0,\"OwnerNode\":\"0\",\"Sequence\":5,\"PreviousTxnID\":\"4BB9CBE44C39DC67A1BE849C7467FE1A6D1F73949EA163C38A0121A15E04FFDE\",\"PreviousTxnLgrSeq\":9038200,\"AcceptedCredentials\":[{\"Credential\":{\"Issuer\":\"rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh\",\"CredentialType\":\"4B5943\"}}],\"index\":\"" + DomainId + "\"}}}";
            runner.mockedRippled.AddResponse("ledger_entry", JsonSerializer.Deserialize<Dictionary<string, object>>(domainJson));
        }

        [TestMethod]
        public async Task TestUGetDomainAccess_PermissionedDex_OwnerHasAccess()
        {
            MockLedgerAndDomain();

            DomainAccessResult result = await runner.client.GetDomainAccess(Owner, DomainId, DomainAccessScope.PermissionedDex);

            Assert.IsTrue(result.HasAccess);
            Assert.IsTrue(result.IsDomainOwner);
            Assert.AreEqual(9038214u, result.LedgerIndex);
        }

        [TestMethod]
        public async Task TestUGetDomainAccess_Credentials_OwnerNeedsACredential()
        {
            MockLedgerAndDomain();

            DomainAccessResult result = await runner.client.GetDomainAccess(Owner, DomainId);

            Assert.IsFalse(result.HasAccess);
            Assert.IsFalse(result.IsDomainOwner);
        }
    }
}
