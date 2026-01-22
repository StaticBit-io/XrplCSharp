using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUDIDDelete
    {
        [TestMethod]
        public async Task TestVerify_Valid_DIDDelete()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "DIDDelete" },
                { "Fee", "12" },
                { "Sequence", 1u }
            };
            await Validation.ValidateDIDDelete(tx);
            await Validation.Validate(tx);
        }
    }
}
