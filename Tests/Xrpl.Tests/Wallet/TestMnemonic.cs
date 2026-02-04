using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;

using Xrpl.Keypairs;
using Xrpl.Wallet;

using XrplTests;

namespace Xrpl.Tests.Wallet.Tests.Mnemonic;

[TestClass]
public class TestMnemonic
{
    private string mnemonic = "assault rare scout seed design extend noble drink talk control guitar quote";
    private string publicKey = "035953FCD81D001CF634EB44A87940F3F98ADF2483D09C914BAED0539BE50F385D";
    private string privateKey = "13FC461CA5799F1357C8130AF703CBA7E9C28E072C6CA8F7DEF8601CDE98F394";

    [TestMethod]
    public void TestDefaultDerivationPath()
    {
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, null, "secp256k1");
        Assert.AreEqual(wallet.PublicKey, publicKey);
        Assert.AreEqual(wallet.PrivateKey, privateKey);
    }

    [TestMethod]
    public void TestInputDerivationPath()
    {
        string derivationPath = "m/44'/144'/0'/0/0";
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, derivationPath, null, "secp256k1");
        Assert.AreEqual(wallet.PublicKey, publicKey);
        Assert.AreEqual(wallet.PrivateKey, privateKey);
    }

    [TestMethod]
    public void TestInputPassPhrase()
    {
        string passPhrase = "my strong password";
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, null, "secp256k1", passPhrase);
        Assert.AreNotEqual(wallet.PublicKey, publicKey);
        Assert.AreNotEqual(wallet.PrivateKey, privateKey);
    }

    [TestMethod]
    public void TestRegularKeypairSeed()
    {
        string masterAddress = "rUAi7pipxGpYfPNg3LtPcf2ApiS8aw9A93";
        string mnemonic = "I IRE BOND BOW TRIO LAID SEAT GOAL HEN IBIS IBIS DARE";
        rKeypair regularKeyPair = new rKeypair
        {
            PublicKey = "0330E7FC9D56BB25D6893BA3F317AE5BCF33B3291BD63DB32654A313222F7FD020",
            PrivateKey = "001ACAAEDECE405B2A958212629E16F2EB46B153EEE94CDD350FDEFF52795525B7",
        };
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, masterAddress, null, "rfc1751", "secp256k1");
        XrplWallet wallet2 = XrplWallet.FromMnemonic(mnemonic, masterAddress, null, "rfc1751", "ed25519");

        Assert.AreEqual(wallet.PublicKey, regularKeyPair.PublicKey);
        Assert.AreEqual(wallet.PrivateKey, regularKeyPair.PrivateKey);
        Assert.AreEqual(wallet.ClassicAddress, masterAddress);
    }

    // This needs to throw an error
    [TestMethod]
    public void TestThrowsB39Seed()
    {
        string mnemonic = "draw attack antique swing base employ blur above palace lucky glide clap pen use illegal";

        Helper.ThrowsException<ArgumentException>(() => XrplWallet.FromMnemonic(mnemonic, null, null, "rfc1751", "ed25519"), "Expected an RFC1751 word, but received 'attack'.");
    }

    [TestMethod]
    public void TestEDPRFC1751SeedLower()
    {
        string mnemonic = "cab beth hank bird mend sign gild any kern hyde chat stub";
        string expectedSeed = "snVB4iTWYqsWZaj1hkvAy1QzqNbAg";

        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic, null, null, "rfc1751", "secp256k1");

        Assert.AreEqual(expectedSeed, wallet.Seed);
    }

    [TestMethod]
    public void TestGenerateMnemonic_Default12Words()
    {
        string[] words = XrplWallet.GenerateMnemonic();
        Assert.AreEqual(12, words.Length);
        foreach (var word in words)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(word));
        }
    }

    [TestMethod]
    public void TestGenerateMnemonic_15Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(15);
        var wallet = XrplWallet.FromMnemonic(string.Join(' ', words));
        Assert.AreEqual(15, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_18Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(18);
        Assert.AreEqual(18, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_21Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(21);
        Assert.AreEqual(21, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_24Words()
    {
        string[] words = XrplWallet.GenerateMnemonic(24);
        Assert.AreEqual(24, words.Length);
    }

    [TestMethod]
    public void TestGenerateMnemonic_InvalidWordCount()
    {
        var ex1 = Helper.ThrowsException<ArgumentException>(() => XrplWallet.GenerateMnemonic(10));
        Assert.IsTrue(ex1.Message.Contains("Invalid word count: 10"));
        
        var ex2 = Helper.ThrowsException<ArgumentException>(() => XrplWallet.GenerateMnemonic(13));
        Assert.IsTrue(ex2.Message.Contains("Invalid word count: 13"));
    }

    [TestMethod]
    public void TestGenerateMnemonic_CreateWalletRoundtrip()
    {
        string[] words = XrplWallet.GenerateMnemonic(24);
        string mnemonic = string.Join(" ", words);
        
        XrplWallet wallet = XrplWallet.FromMnemonic(mnemonic);
        
        Assert.IsNotNull(wallet);
        Assert.IsFalse(string.IsNullOrEmpty(wallet.PublicKey));
        Assert.IsFalse(string.IsNullOrEmpty(wallet.PrivateKey));
        Assert.IsFalse(string.IsNullOrEmpty(wallet.ClassicAddress));
    }

    [TestMethod]
    public void TestGenerateMnemonic_Randomness()
    {
        string[] words1 = XrplWallet.GenerateMnemonic();
        string[] words2 = XrplWallet.GenerateMnemonic();
        
        bool areDifferent = !words1.SequenceEqual(words2);
        Assert.IsTrue(areDifferent, "Two generated mnemonics should be different");
    }
}
