using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUDIDSet
    {
        [TestMethod]
        public async Task TestVerify_Valid_WithData()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "Data", "48656C6C6F" }
            };
            await Validation.ValidateDIDSet(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Valid_WithDIDDocument()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "DIDDocument", "48656C6C6F" }
            };
            await Validation.ValidateDIDSet(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Valid_WithURI()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "URI", "68747470733A2F2F6578616D706C652E636F6D" }
            };
            await Validation.ValidateDIDSet(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Valid_WithAllFields()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "Data", "48656C6C6F" },
                { "DIDDocument", "48656C6C6F" },
                { "URI", "68747470733A2F2F6578616D706C652E636F6D" }
            };
            await Validation.ValidateDIDSet(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Invalid_MissingAllOptionalFields()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDIDSet(tx),
                "DIDSet: must include at least one of Data, DIDDocument, or URI");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_EmptyData()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "Data", "" }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDIDSet(tx),
                "DIDSet: must include at least one of Data, DIDDocument, or URI");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_NullData()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "Data", null }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDIDSet(tx),
                "DIDSet: must include at least one of Data, DIDDocument, or URI");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_DataTooLong()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "Data", new string('A', 514) }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDIDSet(tx),
                "DIDSet: Data must not exceed 256 bytes (512 hex characters)");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_DIDDocumentTooLong()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "DIDDocument", new string('B', 514) }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDIDSet(tx),
                "DIDSet: DIDDocument must not exceed 256 bytes (512 hex characters)");
        }

        [TestMethod]
        public async Task TestVerify_Invalid_URITooLong()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "URI", new string('C', 514) }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateDIDSet(tx),
                "DIDSet: URI must not exceed 256 bytes (512 hex characters)");
        }

        [TestMethod]
        public async Task TestVerify_Valid_MaxLengthData()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "Data", new string('A', 512) }
            };
            await Validation.ValidateDIDSet(tx);
        }
    }
}
