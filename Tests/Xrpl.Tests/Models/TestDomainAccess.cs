using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

using Xrpl.Models.Ledger;
using Xrpl.Sugar;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUDomainAccess
    {
        private static readonly DateTime CloseTime = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        private const uint LedgerIndex = 1000u;
        private const string Issuer = "ra5nK24KXen9AHvsdFTKHSANinZseWnPcX";
        private const string CredentialTypeHex = "6D795F63726564656E7469616C";

        private static LOCredential CreateCredential(bool accepted, DateTime? expiration = null)
        {
            LOCredential credential = new LOCredential
            {
                Subject = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn",
                Issuer = Issuer,
                CredentialType = CredentialTypeHex,
                Expiration = expiration,
                Flags = accepted ? (uint)CredentialFlags.lsfAccepted : 0u
            };
            return credential;
        }

        [TestMethod]
        public void TestEvaluate_AcceptedNotExpired_GrantsAccess()
        {
            List<LOCredential> credentials = new List<LOCredential> { CreateCredential(accepted: true) };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsTrue(result.HasAccess);
            Assert.AreEqual(0, result.InvalidCredentials.Count);
            Assert.AreEqual(LedgerIndex, result.LedgerIndex);
        }

        [TestMethod]
        public void TestEvaluate_NotAccepted_NoAccess_ReportedInvalid()
        {
            List<LOCredential> credentials = new List<LOCredential> { CreateCredential(accepted: false) };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsFalse(result.HasAccess);
            Assert.AreEqual(1, result.InvalidCredentials.Count);
            DomainCredentialStatus status = result.InvalidCredentials[0];
            Assert.AreEqual(Issuer, status.Issuer);
            Assert.AreEqual(CredentialTypeHex, status.CredentialType);
            Assert.IsFalse(status.Accepted);
            Assert.IsFalse(status.Expired);
        }

        [TestMethod]
        public void TestEvaluate_Expired_NoAccess_ReportedExpired()
        {
            List<LOCredential> credentials = new List<LOCredential>
            {
                CreateCredential(accepted: true, expiration: CloseTime.AddSeconds(-1))
            };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsFalse(result.HasAccess);
            Assert.AreEqual(1, result.InvalidCredentials.Count);
            Assert.IsTrue(result.InvalidCredentials[0].Accepted);
            Assert.IsTrue(result.InvalidCredentials[0].Expired);
        }

        [TestMethod]
        public void TestEvaluate_ExpirationEqualToCloseTime_NotExpired()
        {
            // rippled's credentials::checkExpired treats a credential as expired
            // only when close time is strictly greater than Expiration.
            List<LOCredential> credentials = new List<LOCredential>
            {
                CreateCredential(accepted: true, expiration: CloseTime)
            };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsTrue(result.HasAccess);
        }

        [TestMethod]
        public void TestEvaluate_NoExpiration_NeverExpires()
        {
            List<LOCredential> credentials = new List<LOCredential>
            {
                CreateCredential(accepted: true, expiration: null)
            };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsTrue(result.HasAccess);
        }

        [TestMethod]
        public void TestEvaluate_NoMatchingCredentials_NoAccess_EmptyInvalidList()
        {
            List<LOCredential> credentials = new List<LOCredential> { null, null };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsFalse(result.HasAccess);
            Assert.AreEqual(0, result.InvalidCredentials.Count);
        }

        [TestMethod]
        public void TestEvaluate_MixedValidAndInvalid_AccessGranted_InvalidListEmpty()
        {
            // Mirrors the proposed domain_access API from XRPLF/rippled#7743:
            // invalid_credentials is only populated when has_access is false.
            List<LOCredential> credentials = new List<LOCredential>
            {
                CreateCredential(accepted: false),
                null,
                CreateCredential(accepted: true)
            };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsTrue(result.HasAccess);
            Assert.AreEqual(0, result.InvalidCredentials.Count);
        }

        [TestMethod]
        public void TestEvaluate_MultipleInvalid_AllReported()
        {
            List<LOCredential> credentials = new List<LOCredential>
            {
                CreateCredential(accepted: false),
                CreateCredential(accepted: true, expiration: CloseTime.AddDays(-1))
            };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsFalse(result.HasAccess);
            Assert.AreEqual(2, result.InvalidCredentials.Count);
        }

        [TestMethod]
        public void TestEvaluate_MissingFlags_TreatedAsNotAccepted_NoAccess()
        {
            // Regression guard: Flags is nullable (e.g. a caller assembling a credential from
            // NewFields/PreviousFields, where absence is normal). A missing Flags value must not
            // be read as "accepted" - a lifted `!=` returns true when either side is null, unlike
            // a lifted `<`, which returns false, so this is a distinct failure mode from the
            // SignerQuorum case and must be checked explicitly rather than relying on the operator.
            LOCredential credential = new LOCredential
            {
                Subject = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn",
                Issuer = Issuer,
                CredentialType = CredentialTypeHex,
                Expiration = null,
                Flags = null
            };
            List<LOCredential> credentials = new List<LOCredential> { credential };

            DomainAccessResult result = DomainAccessSugar.EvaluateDomainAccess(credentials, CloseTime, LedgerIndex);

            Assert.IsFalse(result.HasAccess);
            Assert.AreEqual(1, result.InvalidCredentials.Count);
            Assert.IsFalse(result.InvalidCredentials[0].Accepted);
        }
    }
}
