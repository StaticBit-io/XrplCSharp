// Mirrors rippled DelegateSet::preflight in
// src/libxrpl/tx/transactors/delegate/DelegateSet.cpp

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;
using Xrpl.Tests.Models.Tests;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// The local checks exist to catch what rippled's preflight would reject, and nothing beyond
    /// it: an extra rule here refuses a transaction the ledger would have accepted.
    /// </summary>
    [TestClass]
    public class TestUDelegateSet
    {
        private const string Owner = "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm";
        private const string Delegate = "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd";

        private static Dictionary<string, object> Permission(object value) =>
            new Dictionary<string, object>
            {
                { "Permission", new Dictionary<string, object> { { "PermissionValue", value } } },
            };

        private static Dictionary<string, object> Transaction(params object[] permissionValues)
        {
            List<object> permissions = new List<object>();
            foreach (object value in permissionValues)
                permissions.Add(Permission(value));

            return new Dictionary<string, object>
            {
                { "TransactionType", "DelegateSet" },
                { "Account", Owner },
                { "Authorize", Delegate },
                { "Sequence", 1337u },
                { "Permissions", permissions },
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            Validation.Validate(Transaction(1u, 65537u));
        }

        [TestMethod]
        public void TestEmptyPermissionsRevokesTheDelegation()
        {
            // rippled's doApply deletes the Delegate ledger object when the array is empty, so an
            // empty Permissions is how a grant is withdrawn. Refusing it here blocked the only way
            // to revoke a delegation through this SDK.
            Validation.Validate(Transaction());
        }

        [TestMethod]
        public void TestAuthorizeMustDifferFromAccount()
        {
            Dictionary<string, object> tx = Transaction(1u);
            tx["Authorize"] = Owner;

            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(tx),
                "DelegateSet: Authorize and Account must be different");
        }

        [TestMethod]
        public void TestMissingAuthorize()
        {
            Dictionary<string, object> tx = Transaction(1u);
            tx.Remove("Authorize");

            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(tx),
                "DelegateSet: missing field Authorize");
        }

        [TestMethod]
        public void TestDuplicatePermissionValue()
        {
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(Transaction(1u, 1u)),
                "DelegateSet: duplicate PermissionValue '1'");
        }

        [TestMethod]
        public void TestTooManyPermissions()
        {
            object[] eleven = new object[11];
            for (uint i = 0; i < eleven.Length; i++)
                eleven[i] = i + 1;

            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(Transaction(eleven)),
                "DelegateSet: Permissions must have at most 10 entries");
        }

        [TestMethod]
        public void TestNonDelegableTypeByName()
        {
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(Transaction("VaultCreate")),
                "DelegateSet: transaction type 'VaultCreate' cannot be delegated");
        }

        [TestMethod]
        public void TestNonDelegableTypeByNumericValue()
        {
            // AccountSet is transaction type 3, so its permission value is 4. The value is the
            // form the typed model produces, and the check has to see through it.
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(Transaction(4u)),
                "DelegateSet: transaction type 'AccountSet' cannot be delegated");
        }

        [TestMethod]
        public void TestGranularPermissionOnNonDelegableTypeIsAllowed()
        {
            // AccountDomainSet (65540) authorizes part of AccountSet, which is not delegable as a
            // whole. rippled allows the granular permission anyway, so the deny list must not
            // reach it - which it does not, because 65540 resolves to its own name.
            Validation.Validate(Transaction(65540u));
            Validation.Validate(Transaction("AccountDomainSet"));
        }

        [TestMethod]
        public void TestDenyListMatchesRippledMacro()
        {
            // The list is hand-held, so this is what keeps it from drifting: it is compared with
            // the vendored transactions.macro, the same fixture TestUTxFormatConformance reads.
            // Re-pinning the fixture without extending the list turns this red by name.
            HashSet<string> fromMacro = RippledTransactionFormats.NonDelegableTransactions();

            CollectionAssert.AreEquivalent(
                fromMacro.OrderBy(name => name).ToList(),
                Validation.NonDelegableTransactions.OrderBy(name => name).ToList(),
                "the DelegateSet deny list no longer matches rippled's NotDelegable types");
        }
    }
}
