using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUPermissionedDomainDelete
    {
        [TestMethod]
        public void TestVerify_Valid_WithDomainID()
        {
            var tx = new Dictionary<string, object>
            {
                { "Account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
                { "TransactionType", "PermissionedDomainDelete" },
                { "Fee", "10" },
                { "Sequence", 392u },
                { "DomainID", "77D6234D074E505024D39C04C3F262997B773719AB29ACFA83119E4210328776" }
            };
            Validation.ValidatePermissionedDomainDelete(tx);
            Validation.Validate(tx);
        }

        [TestMethod]
        public void TestVerify_Invalid_MissingDomainID()
        {
            var tx = new Dictionary<string, object>
            {
                { "Account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
                { "TransactionType", "PermissionedDomainDelete" },
                { "Fee", "10" },
                { "Sequence", 392u }
            };
            Helper.ThrowsException<ValidationException>(
                () => Validation.ValidatePermissionedDomainDelete(tx),
                "PermissionedDomainDelete: DomainID is required");
        }

        [TestMethod]
        public void TestVerify_Invalid_EmptyDomainID()
        {
            var tx = new Dictionary<string, object>
            {
                { "Account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
                { "TransactionType", "PermissionedDomainDelete" },
                { "Fee", "10" },
                { "Sequence", 392u },
                { "DomainID", "" }
            };
            Helper.ThrowsException<ValidationException>(
                () => Validation.ValidatePermissionedDomainDelete(tx),
                "PermissionedDomainDelete: DomainID is required");
        }
    }
}
