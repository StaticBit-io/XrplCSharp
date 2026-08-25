using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

namespace Xrpl.Tests.Client.Json
{
    /// <summary>
    /// A transaction held in a variable typed as its interface serializes like the transaction it
    /// is - issue #127.
    /// </summary>
    /// <remarks>
    /// <para>
    /// System.Text.Json picks a converter by the <b>declared</b> type. The attribute sat on the
    /// abstract class, so a variable declared as <c>ITransactionRequest</c> did not get it and was
    /// written as the interface: every field of the actual transaction type gone, the payment left
    /// with no <c>Amount</c> and no <c>Destination</c>, and no exception or warning anywhere.
    /// </para>
    /// <para>
    /// Interfaces are what one declares in the places where the transaction type is the caller's
    /// choice - factories, submission pipelines, wallet wrappers. It happened to work as long as
    /// everything went through <c>XrplJsonOptions.Default</c>, whose converter list makes up for
    /// the missing attribute, and stopped the moment someone serialized with their own options.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUInterfaceSerialization
    {
        private static Payment APayment() => new Payment
        {
            Account = "r4f4xLpXJtCh9PwdzsQ6KYwLevVnBpJV6f",
            Destination = "rQUSmV11JUe71qEJNsTQcw4rqzYDyEHZEG",
            Amount = new Currency { ValueAsXrp = 5 },
        };

        /// <summary>
        /// The same transaction, declared two ways, must serialize to the same JSON - with the
        /// caller's own options, which is where this used to diverge.
        /// </summary>
        [TestMethod]
        public void TestUInterfaceAndConcreteTypeSerializeAlike()
        {
            Payment payment = APayment();
            ITransactionRequest asInterface = payment;

            string viaInterface = JsonSerializer.Serialize(asInterface);
            string viaConcrete = JsonSerializer.Serialize(payment);

            Assert.AreEqual(
                viaConcrete,
                viaInterface,
                "A variable's declared type must not decide what reaches the wire.");
        }

        /// <summary>
        /// Named individually, because "the same JSON" says nothing about whether either is right.
        /// </summary>
        [TestMethod]
        public void TestUInterfaceKeepsTheFieldsOfTheActualTransaction()
        {
            ITransactionRequest asInterface = APayment();

            string json = JsonSerializer.Serialize(asInterface);

            StringAssert.Contains(json, "Destination", $"A payment without a destination is not a payment: {json}");
            StringAssert.Contains(json, "Amount", $"A payment without an amount is not a payment: {json}");
        }

        /// <summary>
        /// And the transaction type reaches the wire as a name, not as the number behind the enum.
        /// </summary>
        /// <remarks>
        /// The converter that spells it out sits on the class property too, so it went missing by
        /// the same route: <c>"TransactionType":16</c> instead of <c>"Payment"</c>.
        /// </remarks>
        [TestMethod]
        public void TestUInterfaceWritesTheTransactionTypeAsAName()
        {
            ITransactionRequest asInterface = APayment();

            string json = JsonSerializer.Serialize(asInterface);

            // The pair, not the two halves separately: asserting only that "Payment" appears
            // somewhere would also pass if the field held the enum's number and the name turned up
            // in an unrelated place, and asserting the absence of ":16" would stop testing anything
            // the day that number changes.
            StringAssert.Contains(
                json,
                "\"TransactionType\":\"Payment\"",
                $"a node reads the name, not the number behind the enum: {json}");
        }

        /// <summary>
        /// The signature fields keep the names the protocol uses.
        /// </summary>
        /// <remarks>
        /// The class declares <c>[JsonPropertyName("SigningPubKey")] SigningPublicKey</c>; the
        /// interface declares the property without the attribute. Serialized as the interface, the
        /// transaction carried <c>SigningPublicKey</c> and <c>TransactionSignature</c> - names no
        /// node knows.
        /// </remarks>
        [TestMethod]
        public void TestUInterfaceWritesTheProtocolNamesForSignatureFields()
        {
            Payment payment = APayment();
            payment.SigningPublicKey = "ED9434799226374926EDA3B54B1B461B4ABF7237962EAE18528FEA67595397FA32";
            payment.TransactionSignature = "12345678";
            ITransactionRequest asInterface = payment;

            string json = JsonSerializer.Serialize(asInterface);

            StringAssert.Contains(json, "SigningPubKey", $"the protocol's name for the key: {json}");
            StringAssert.Contains(json, "TxnSignature", $"the protocol's name for the signature: {json}");
            Assert.IsFalse(json.Contains("SigningPublicKey"), $"the C# name must not reach the wire: {json}");
            Assert.IsFalse(json.Contains("TransactionSignature"), $"the C# name must not reach the wire: {json}");
        }

        /// <summary>
        /// Reading is the other half of the same attribute, and it works through the interface too.
        /// </summary>
        /// <remarks>
        /// Before, deserializing into a variable of this type with anything but the SDK's options
        /// had no converter to reach for and no way to build an interface. The same declaration
        /// that fixes writing is what makes this possible, so it is tested rather than assumed.
        /// </remarks>
        [TestMethod]
        public void TestUAnInterfaceVariableRoundTrips()
        {
            string json = JsonSerializer.Serialize<ITransactionRequest>(APayment());

            ITransactionRequest restored = JsonSerializer.Deserialize<ITransactionRequest>(json);

            Assert.IsInstanceOfType<Payment>(
                restored,
                "The discriminator in the JSON is what decides the type, and it says Payment.");

            Payment payment = (Payment)restored;
            Assert.AreEqual("rQUSmV11JUe71qEJNsTQcw4rqzYDyEHZEG", payment.Destination);
            Assert.IsNotNull(payment.Amount, "An amount that survived the trip out must survive the trip back.");
        }

        /// <summary>
        /// The SDK's own options were never the problem, and must stay unaffected.
        /// </summary>
        [TestMethod]
        public void TestUTheSdkOptionsStillSerializeTheSameWay()
        {
            Payment payment = APayment();
            ITransactionRequest asInterface = payment;

            string viaInterface = JsonSerializer.Serialize(asInterface, XrplJsonOptions.Default);
            string viaConcrete = JsonSerializer.Serialize(payment, XrplJsonOptions.Default);

            Assert.AreEqual(viaConcrete, viaInterface);
            StringAssert.Contains(viaInterface, "\"Payment\"");
            StringAssert.Contains(viaInterface, "Destination");
        }
    }
}
