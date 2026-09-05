using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Exceptions;
using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests
{
    /// <summary>
    /// Reading the faucet's answer. The body is a third party's HTTP response and the only part
    /// of funding a wallet that can be exercised without a network, which is the reason these
    /// exist: every way the response can disappoint used to arrive as the same flat message, or
    /// as a NullReferenceException from the middle of the read.
    /// </summary>
    [TestClass]
    public class TestUFaucetResponse
    {
        [TestMethod]
        public void ReadFaucetAddress_TakesTheAddressFromAWellFormedBody()
        {
            string body = @"{
                ""account"": {
                    ""xAddress"": ""T7dRN2ktZGYSTgFdCzYYVbdKPKTvXbGgfe1MJfBLnkYbUQK"",
                    ""classicAddress"": ""rGmaiHAmQ4Kmoc9zAdKQ4rr8YLQGDRXWmE"",
                    ""secret"": ""sEd7f3s4YGCyMLpqBBSGw6dHDGXQqL9""
                },
                ""amount"": 100,
                ""balance"": 100
            }";

            Assert.AreEqual("rGmaiHAmQ4Kmoc9zAdKQ4rr8YLQGDRXWmE", WalletSugar.ReadFaucetAddress(body));
        }

        [TestMethod]
        public void ReadFaucetAddress_WithNoAccount_SaysSoAndQuotesTheBody()
        {
            string body = @"{""error"": ""Rate limit exceeded""}";

            XRPLFaucetException ex = Assert.ThrowsExactly<XRPLFaucetException>(() => WalletSugar.ReadFaucetAddress(body));
            StringAssert.Contains(ex.Message, "no account address");
            StringAssert.Contains(ex.Message, "Rate limit exceeded",
                "the body is what a reader needs to see - a rate limit reads nothing like a broken faucet");
        }

        [TestMethod]
        public void ReadFaucetAddress_WithAnAccountButNoClassicAddress_IsRefused()
        {
            string body = @"{""account"": {""xAddress"": ""T7dRN2ktZGYSTgFdCzYYVbdKPKTvXbGgfe1MJfBLnkYbUQK""}}";

            Assert.ThrowsExactly<XRPLFaucetException>(() => WalletSugar.ReadFaucetAddress(body));
        }

        /// <summary>
        /// A null JSON literal deserializes to a null object rather than throwing, which is how
        /// this used to reach <c>faucetWallet.Account</c> and fail as a NullReferenceException.
        /// </summary>
        [TestMethod]
        public void ReadFaucetAddress_WithANullBody_IsAFaucetFailureNotANullReference()
        {
            XRPLFaucetException ex = Assert.ThrowsExactly<XRPLFaucetException>(() => WalletSugar.ReadFaucetAddress("null"));
            StringAssert.Contains(ex.Message, "no account address");
            Assert.IsNull(ex.InnerException, "nothing threw here - the deserializer simply handed back null");
        }

        [TestMethod]
        public void ReadFaucetAddress_WithSomethingThatIsNotJson_KeepsTheParseFailure()
        {
            XRPLFaucetException ex = Assert.ThrowsExactly<XRPLFaucetException>(
                () => WalletSugar.ReadFaucetAddress("<html><body>502 Bad Gateway</body></html>"));

            Assert.IsInstanceOfType<System.Text.Json.JsonException>(ex.InnerException,
                "the parse failure is the cause and has to survive the wrapping");
        }

        [TestMethod]
        public void FaucetException_KeepsTheCauseItWasGiven()
        {
            InvalidOperationException cause = new InvalidOperationException("the socket went away");

            XRPLFaucetException ex = new XRPLFaucetException("could not reach the faucet", cause);

            Assert.AreSame(cause, ex.InnerException);
            Assert.AreEqual("could not reach the faucet", ex.Message);
        }
    }
}
