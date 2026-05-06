using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

using XrplTests;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Unit tests for OracleDelete transaction validation.
    /// </summary>
    [TestClass]
    public class TestUOracleDelete
    {
        /// <summary>
        /// Tests that a valid OracleDelete transaction passes validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Valid_OracleDelete()
        {
            var tx = new Dictionary<string, object>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleDelete" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u }
            };
            await Validation.ValidateOracleDelete(tx);
            await Validation.Validate(tx);
        }

        /// <summary>
        /// Tests that OracleDelete without OracleDocumentID fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_MissingOracleDocumentID()
        {
            var tx = new Dictionary<string, object>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleDelete" },
                { "Fee", "12" },
                { "Sequence", 1u }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleDelete(tx),
                "OracleDelete: missing field OracleDocumentID");
        }

        /// <summary>
        /// Tests that OracleDelete with null OracleDocumentID fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_NullOracleDocumentID()
        {
            var tx = new Dictionary<string, object>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleDelete" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", null }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleDelete(tx),
                "OracleDelete: missing field OracleDocumentID");
        }

        /// <summary>
        /// Tests that OracleDelete with zero OracleDocumentID passes validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Valid_ZeroOracleDocumentID()
        {
            var tx = new Dictionary<string, object>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleDelete" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 0u }
            };
            await Validation.ValidateOracleDelete(tx);
        }
    }
}
