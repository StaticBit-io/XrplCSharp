using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests.Mnemonic;

[TestClass]
public class TestUFromPrivateKey
{
    private string mnemonic = "assault rare scout seed design extend noble drink talk control guitar quote";
    private string publicKey = "035953FCD81D001CF634EB44A87940F3F98ADF2483D09C914BAED0539BE50F385D";
    private string privateKey = "13FC461CA5799F1357C8130AF703CBA7E9C28E072C6CA8F7DEF8601CDE98F394";
    private string seed = "ssL9dv2W5RK8L3tuzQxYY6EaZhSxW";

    [TestMethod]
    public void TestSECPAlgorithmSeed()
    {
        XrplWallet wallet = XrplWallet.FromSeed(seed, null, "secp256k1");
        XrplWallet wallet2 = XrplWallet.FromPrivateKey(wallet.PrivateKey);

        Assert.AreEqual(wallet.PublicKey, wallet2.PublicKey);
        Assert.AreEqual(wallet.PrivateKey, wallet2.PrivateKey);
        Assert.AreEqual(wallet.ClassicAddress, wallet2.ClassicAddress);
    }

    [TestMethod]
    public void TestEDAlgorithmSeed()
    {
        XrplWallet wallet = XrplWallet.FromSeed(seed, null, "ed25519");
        XrplWallet wallet2 = XrplWallet.FromPrivateKey(wallet.PrivateKey);

        Assert.AreEqual(wallet.PublicKey, wallet2.PublicKey);
        Assert.AreEqual(wallet.PrivateKey, wallet2.PrivateKey);
        Assert.AreEqual(wallet.ClassicAddress, wallet2.ClassicAddress);
    }

    [TestMethod]
    public void TestFromMnemonic()
    {
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, null, "secp256k1");
        XrplWallet wallet2 = XrplWallet.FromPrivateKey(wallet.PrivateKey);
        Assert.AreEqual(wallet.PublicKey, wallet2.PublicKey);
        Assert.AreEqual(wallet.PrivateKey, wallet2.PrivateKey);
        Assert.AreEqual(wallet.ClassicAddress, wallet2.ClassicAddress);
    }

    [TestMethod]
    public void Test_With_masterAddress()
    {
        string masterAddress = "rUAi7pipxGpYfPNg3LtPcf2ApiS8aw9A93";
        string mnemonic = "I IRE BOND BOW TRIO LAID SEAT GOAL HEN IBIS IBIS DARE";

        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, masterAddress, null, "rfc1751", "secp256k1");
        XrplWallet wallet1 = XrplWallet.FromPrivateKey(wallet.PrivateKey, masterAddress);

        Assert.AreEqual(wallet.PublicKey, wallet1.PublicKey);
        Assert.AreEqual(wallet.PrivateKey, wallet1.PrivateKey);
        Assert.AreEqual(wallet.ClassicAddress, wallet1.ClassicAddress);
    }

    [TestMethod]
    public void TestFrom_rfc1751()
    {
        string mnemonic = "I IRE BOND BOW TRIO LAID SEAT GOAL HEN IBIS IBIS DARE";
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, "rfc1751", "secp256k1");
        XrplWallet wallet1 = XrplWallet.FromPrivateKey(wallet.PrivateKey, null);
        XrplWallet wallet2 = XrplWallet.FromMnemonic(mnemonic, null, null, "rfc1751", "ed25519");
        XrplWallet wallet21 = XrplWallet.FromPrivateKey(wallet2.PrivateKey, null);

        Assert.AreEqual(wallet.PublicKey, wallet1.PublicKey);
        Assert.AreEqual(wallet.PrivateKey, wallet1.PrivateKey);
        Assert.AreEqual(wallet.ClassicAddress, wallet1.ClassicAddress);

        Assert.AreEqual(wallet2.PublicKey, wallet21.PublicKey);
        Assert.AreEqual(wallet2.PrivateKey, wallet21.PrivateKey);
        Assert.AreEqual(wallet2.ClassicAddress, wallet21.ClassicAddress);
    }

}