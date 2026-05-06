using NBitcoin;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Keypairs;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Utils.Hashes;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/Wallet/index.ts

namespace Xrpl.Wallet
{
    public class SignatureResult
    {
        [JsonPropertyName("tx_blob")]
        public string TxBlob { get; set; }

        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        public SignatureResult(string txBlob, string hash)
        {
            TxBlob = txBlob;
            Hash = hash;
        }

        public Dictionary<string, object> GetTxDictionary()
        {
            if (TxBlob == null)
            {
                throw new NullReferenceException(nameof(TxBlob));
            }

            var dic = XrplBinaryCodec.Decode(TxBlob);
            dic["hash"] = Hash; // add hash to the dictionary for convenience
            return JsonSerializer.Deserialize<Dictionary<string, object>>(dic.ToString(), XrplJsonOptions.Default);
        }

        public ITransactionRequest GetTx()
        {
            if (TxBlob == null)
            {
                throw new NullReferenceException(nameof(TxBlob));
            }
            return JsonSerializer.Deserialize<TransactionRequest>(
                XrplBinaryCodec.Decode(TxBlob).ToString(), XrplJsonOptions.Default);
        }
    }
    public enum TextWalletKdf
    {
        Sha256 = 0,
        Pbkdf2 = 1,
    }

    public class XrplWallet
    {

        public static string DEFAULT_ALGORITHM = Ed25519;
        public const string Ed25519 = "ed25519";
        public const string Secp256k1 = "secp256k1";

        private static readonly Lazy<string[]> _bip39WordlistCache = new Lazy<string[]>(() =>
        {
            var words = new string[2048];
            for (int i = 0; i < 2048; i++)
                words[i] = Wordlist.English.GetWordAtIndex(i);
            return words;
        });

        public readonly string PublicKey;
        public readonly string PrivateKey;
        public readonly string ClassicAddress;
        public readonly string Seed;

        /// <summary>
        /// Creates a new Wallet.
        /// </summary>
        /// <param name="publicKey">The public key for the account.</param>
        /// <param name="privateKey">The private key used for signing transactions for the account.</param>
        /// <param name="masterAddress">Include if a Wallet uses a Regular Key Pair. It must be the master address of the account.</param>
        /// <param name="seed">The seed used to derive the account keys.</param>
        public XrplWallet(string publicKey, string privateKey, string? masterAddress = null, string? seed = null)
        {
            this.PublicKey = publicKey;
            this.PrivateKey = privateKey;
            this.ClassicAddress = masterAddress ?? XrplKeypairs.DeriveAddress(publicKey);
            this.Seed = seed;
        }

        /// <summary>
        /// Generates a new Wallet using a generated seed.
        /// </summary>
        /// <param name="algorithm">The digital signature algorithm to generate an address for.</param>
        /// <returns>A new Wallet derived from a generated seed.</returns>
        public static XrplWallet Generate(string algorithm = Ed25519)
        {
            string seed = XrplKeypairs.GenerateSeed(null, algorithm);
            return XrplWallet.FromSeed(seed, null, algorithm);
        }
        /// <summary>
        /// Derives a wallet from a seed.
        /// </summary>
        /// <param name="seed">A string used to generate a keypair (publicKey/privateKey) to derive a wallet.</param>
        /// <param name="algorithm">The digital signature algorithm to generate an address for.</param>
        /// <param name="masterAddress">Include if a Wallet uses a Regular Key Pair. It must be the master address of the account.</param>
        /// <returns>A Wallet derived from a seed.</returns>
        public static XrplWallet FromSeed(string seed, string? masterAddress = null, string? algorithm = null)
        {
            return XrplWallet.DeriveWallet(seed, masterAddress, algorithm);
        }

        /// <summary>
        /// Creates a new instance of the XrplWallet class using the specified private key and an optional master
        /// address.
        /// </summary>
        /// <remarks>The method derives the public key from the provided private key using XRPL keypair
        /// functionality. Supplying an invalid private key will result in an exception during wallet creation. Ensure
        /// that the private key is valid and securely managed.</remarks>
        /// <param name="privateKey">The private key used to derive the wallet's public key. Must be a valid XRPL private key format and should
        /// be kept secure.</param>
        /// <param name="masterAddress">An optional master address associated with the wallet. If provided, it is used as part of the wallet's
        /// initialization; otherwise, the wallet is initialized without a master address.</param>
        /// <returns>An XrplWallet instance containing the derived public key, the provided private key, and the optional master
        /// address.</returns>
        public static XrplWallet FromPrivateKey(string privateKey, string? masterAddress = null)
        {
            var publicKey = XrplKeypairs.DerivePublicKeyFromPrivateKey(privateKey);
            return new XrplWallet(publicKey, privateKey, masterAddress);
        }

        /// <summary>
        /// An array of random numbers to generate a seed used to derive a wallet.
        /// </summary>
        /// <param name="algorithm">The digital signature algorithm to generate an address for.</param>
        /// <param name="masterAddress">Include if a Wallet uses a Regular Key Pair. It must be the master address of the account.</param>
        /// <returns>A Wallet derived from an entropy.</returns>
        public static XrplWallet FromEntropy(byte[] entropy, string? masterAddress = null, string? algorithm = null)
        {
            string falgorithm = algorithm ?? XrplWallet.DEFAULT_ALGORITHM;
            string seed = XrplKeypairs.GenerateSeed(entropy, falgorithm);
            return XrplWallet.DeriveWallet(seed, masterAddress, falgorithm);
        }

        public static XrplWallet FromMnemonic(string mnemonic,
            string? masterAddress = null,
            string? derivationPath = null,
            string? encoding = null,
            string? algorithm = null,
            string? passphrase = null)
        {

            if (encoding == "rfc1751")
            {
                return FromRFC1751Mnemonic(mnemonic, masterAddress, algorithm);
            }

            if (!IsValidBip39Mnemonic(mnemonic))
            {
                throw new ValidationException("Unable to parse the given mnemonic using bip39 encoding");
            }

            var masterNode = new Mnemonic(mnemonic).DeriveExtKey(passphrase);
            //var masterNode = new ExtKey(seed);
            var node = masterNode.Derive(new KeyPath(derivationPath ?? "m/44'/144'/0'/0/0"));

            var publicKey = node.PrivateKey.PubKey.ToHex().ToUpper();
            var privateKey = node.PrivateKey.ToHex().ToUpper();
            return new XrplWallet(publicKey, privateKey, masterAddress);
        }
        private static XrplWallet FromRFC1751Mnemonic(string mnemonic, string? masterAddress = null, string? algorithm = null)
        {
            var seed = RFC1751.RFC1751MnemonicToKey(mnemonic);
            var encodeAlgorithm = algorithm == Ed25519 ? Ed25519 : Secp256k1;
            var encodedSeed = XrplCodec.EncodeSeed(seed, encodeAlgorithm);
            return FromSeed(encodedSeed, masterAddress, algorithm);
        }

        private static bool IsValidBip39Mnemonic(string mnemonic)
        {
            try
            {
                var mnemo = new Mnemonic(mnemonic);
                return mnemo.IsValidChecksum;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a random BIP-39 mnemonic phrase.
        /// <para>
        /// BIP-39 defines a standard for mnemonic phrases - human-readable word sequences
        /// that encode cryptographic entropy. The words are selected from a standardized
        /// 2048-word English wordlist.
        /// </para>
        /// <para>
        /// The number of words determines the entropy strength:
        /// <list type="bullet">
        ///   <item><description>12 words = 128 bits of entropy (standard)</description></item>
        ///   <item><description>15 words = 160 bits of entropy</description></item>
        ///   <item><description>18 words = 192 bits of entropy</description></item>
        ///   <item><description>21 words = 224 bits of entropy</description></item>
        ///   <item><description>24 words = 256 bits of entropy (maximum)</description></item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="wordCount">The number of words to generate (12, 15, 18, 21, or 24). Default is 12.</param>
        /// <returns>An array of mnemonic words.</returns>
        /// <exception cref="ArgumentException">Thrown when wordCount is not 12, 15, 18, 21, or 24.</exception>
        /// <remarks>
        /// Reference: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki
        /// </remarks>
        /// <example>
        /// <code>
        /// // Generate 12-word mnemonic (default)
        /// string[] words12 = XrplWallet.GenerateMnemonic();
        /// 
        /// // Generate 24-word mnemonic for maximum security
        /// string[] words24 = XrplWallet.GenerateMnemonic(24);
        /// 
        /// // Create wallet from mnemonic
        /// var wallet = XrplWallet.FromMnemonic(string.Join(" ", words24));
        /// </code>
        /// </example>
        public static string[] GenerateMnemonic(int wordCount = 12)
        {
            WordCount nbWordCount = wordCount switch
            {
                12 => WordCount.Twelve,
                15 => WordCount.Fifteen,
                18 => WordCount.Eighteen,
                21 => WordCount.TwentyOne,
                24 => WordCount.TwentyFour,
                _ => throw new ArgumentException(
                    $"Invalid word count: {wordCount}. Must be one of: 12, 15, 18, 21, 24.",
                    nameof(wordCount))
            };

            var mnemonic = new Mnemonic(Wordlist.English, nbWordCount);
            return mnemonic.Words;
        }

        /// <summary>
        /// Validates whether a word exists in the BIP-39 English wordlist.
        /// <para>
        /// The BIP-39 standard defines a fixed set of 2048 English words used for mnemonic phrases.
        /// This method checks if a given word is present in that wordlist.
        /// Use this for real-time validation as the user types each word.
        /// </para>
        /// </summary>
        /// <param name="word">The word to validate (case-insensitive).</param>
        /// <returns><c>true</c> if the word exists in the BIP-39 English wordlist; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Note: In BIP-39, any valid word can appear at any position in the mnemonic.
        /// Position-level correctness can only be verified via checksum validation
        /// after all words have been entered (see <see cref="ValidateMnemonicChecksum"/>).
        /// <para>Reference: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// bool valid = XrplWallet.ValidateMnemonicWord("abandon"); // true
        /// bool invalid = XrplWallet.ValidateMnemonicWord("xyz123"); // false
        /// </code>
        /// </example>
        public static bool ValidateMnemonicWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;
            return Wordlist.English.WordExists(word.Trim().ToLowerInvariant(), out _);
        }

        /// <summary>
        /// Suggests BIP-39 words similar to the given input for autocomplete and typo correction.
        /// <para>
        /// Returns matching words in priority order: exact prefix matches first (sorted alphabetically),
        /// then fuzzy matches by Levenshtein distance (for typo correction).
        /// Duplicates are removed so prefix matches are not repeated in fuzzy results.
        /// </para>
        /// </summary>
        /// <param name="input">The partial or misspelled word to find suggestions for.</param>
        /// <param name="maxSuggestions">Maximum number of suggestions to return. Default is 5.</param>
        /// <returns>
        /// An array of suggested words from the BIP-39 English wordlist, ordered by relevance.
        /// Returns an empty array if input is null or empty.
        /// </returns>
        /// <remarks>
        /// The algorithm uses two strategies:
        /// <list type="number">
        ///   <item><description>Prefix matching: words that start with the input string.</description></item>
        ///   <item><description>Levenshtein distance: words within edit distance 2 of the input (for typo correction).</description></item>
        /// </list>
        /// <para>Reference: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Prefix matching
        /// string[] suggestions = XrplWallet.SuggestMnemonicWords("aban");
        /// // Returns: ["abandon", "ability", ...] — words starting with "aban"
        ///
        /// // Typo correction
        /// string[] typoFix = XrplWallet.SuggestMnemonicWords("abandonn");
        /// // Returns: ["abandon"] — corrects the typo
        /// </code>
        /// </example>
        public static string[] SuggestMnemonicWords(string input, int maxSuggestions = 5)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            string normalized = input.Trim().ToLowerInvariant();
            var allWords = _bip39WordlistCache.Value;

            var prefixMatches = allWords
                .Where(w => w.StartsWith(normalized, StringComparison.Ordinal))
                .OrderBy(w => w)
                .ToList();

            if (prefixMatches.Count >= maxSuggestions)
                return prefixMatches.Take(maxSuggestions).ToArray();

            var prefixSet = new HashSet<string>(prefixMatches);
            var fuzzyMatches = allWords
                .Where(w => !prefixSet.Contains(w))
                .Select(w => new { Word = w, Distance = LevenshteinDistance(normalized, w) })
                .Where(x => x.Distance <= 2)
                .OrderBy(x => x.Distance)
                .ThenBy(x => x.Word)
                .Select(x => x.Word)
                .ToList();

            var result = new List<string>(prefixMatches);
            result.AddRange(fuzzyMatches);
            return result.Take(maxSuggestions).ToArray();
        }

        private static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
            if (string.IsNullOrEmpty(target)) return source.Length;

            int sourceLength = source.Length;
            int targetLength = target.Length;
            var distance = new int[sourceLength + 1, targetLength + 1];

            for (int i = 0; i <= sourceLength; i++) distance[i, 0] = i;
            for (int j = 0; j <= targetLength; j++) distance[0, j] = j;

            for (int i = 1; i <= sourceLength; i++)
            {
                for (int j = 1; j <= targetLength; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[sourceLength, targetLength];
        }

        /// <summary>
        /// Validates the checksum of a complete BIP-39 mnemonic phrase.
        /// <para>
        /// In BIP-39, the last word of a mnemonic contains checksum bits derived from the
        /// SHA-256 hash of the entropy. This method verifies that the checksum is correct,
        /// which confirms that all words are valid and in the correct order.
        /// </para>
        /// </summary>
        /// <param name="words">The complete mnemonic phrase as an array of words.</param>
        /// <returns><c>true</c> if the mnemonic has a valid checksum; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method performs three levels of validation:
        /// <list type="number">
        ///   <item><description>Word count: must be 12, 15, 18, 21, or 24.</description></item>
        ///   <item><description>Word validity: all words must exist in the BIP-39 English wordlist.</description></item>
        ///   <item><description>Checksum: the last word's checksum bits must match the SHA-256 hash of the entropy.</description></item>
        /// </list>
        /// <para>
        /// Call this method after the user has entered all mnemonic words.
        /// For per-word validation during input, use <see cref="ValidateMnemonicWord"/>.
        /// </para>
        /// <para>Reference: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// string[] words = { "assault", "rare", "scout", "seed", "design", "extend",
        ///                     "noble", "drink", "talk", "control", "guitar", "quote" };
        /// bool valid = XrplWallet.ValidateMnemonicChecksum(words); // true
        ///
        /// words[11] = "abandon"; // corrupt last word
        /// bool invalid = XrplWallet.ValidateMnemonicChecksum(words); // false
        /// </code>
        /// </example>
        public static bool ValidateMnemonicChecksum(string[] words)
        {
            if (words == null || words.Length == 0)
                return false;

            int count = words.Length;
            if (count != 12 && count != 15 && count != 18 && count != 21 && count != 24)
                return false;

            string sentence = string.Join(" ", words);
            try
            {
                var mnemo = new Mnemonic(sentence);
                return mnemo.IsValidChecksum;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Derive a Wallet from a seed.
        /// </summary>
        /// <param name="seed">The seed used to derive the wallet.</param>
        /// <param name="algorithm">The digital signature algorithm to generate an address for.</param>
        /// <param name="masterAddress">Include if a Wallet uses a Regular Key Pair. It must be the master address of the account.</param>
        /// <returns>A Wallet derived from the seed.</returns>
        private static XrplWallet DeriveWallet(string seed, string? masterAddress = null, string? algorithm = null)
        {
            IXrplKeyPair keypair = XrplKeypairs.DeriveKeypair(seed, algorithm);
            return new XrplWallet(keypair.Id(), keypair.Pk(), masterAddress, seed);
        }
        /// <summary>
        /// Creates a Wallet from xumm numbers.
        /// </summary>
        /// <returns>A Wallet from xumm numbers.</returns>
        public static XrplWallet FromXummNumbers(string[] numbers, string algorithm = Secp256k1, string? masterAddress = null)
        {
            byte[] entropy = XummExtension.EntropyFromXummNumbers(numbers);
            return FromEntropy(entropy, masterAddress, algorithm);
        }

        /// <summary>
        /// Creates a Wallet from a space-separated secret numbers string.
        /// Accepts formats like "554872 394230 209376 323698 140250 387423 652803 258676".
        /// </summary>
        /// <param name="secretString">Space-separated secret numbers string (8 groups of 6 digits)</param>
        /// <param name="algorithm">The digital signature algorithm to use. Default is secp256k1.</param>
        /// <returns>A Wallet created from the secret numbers.</returns>
        public static XrplWallet FromSecretString(string secretString, string algorithm = Secp256k1)
        {
            string[] numbers = XummExtension.ParseSecretString(secretString);
            return FromXummNumbers(numbers, algorithm);
        }

        /// <summary>
        /// Gets the Secret Numbers representation of this wallet's seed.
        /// Returns 8 groups of 6 digits each, where 5 digits are entropy and 1 digit is checksum.
        /// </summary>
        /// <returns>Array of 8 secret number strings, or null if the wallet was not created from a seed.</returns>
        public string[] GetSecretNumbers()
        {
            if (string.IsNullOrEmpty(Seed))
                return null;

            var decoded = XrplCodec.DecodeSeed(Seed);
            return XummExtension.EntropyToSecretNumbers(decoded.Bytes);
        }

        /// <summary>
        /// Gets the Secret Numbers as a formatted string with spaces between groups.
        /// </summary>
        /// <returns>Space-separated secret numbers string, or null if the wallet was not created from a seed.</returns>
        public string GetSecretString()
        {
            var numbers = GetSecretNumbers();
            return numbers != null ? string.Join(" ", numbers) : null;
        }

        /// <summary>
        /// Creates a Wallet from any text.
        /// </summary>
        /// <param name="text">any text to generate wallet</param>
        /// <param name="algorithm">The digital signature algorithm to generate an address for.</param>
        /// <param name="salt">user salt as a password</param>
        /// <param name="caseInsensitive">is case-insensitive</param>
        /// <param name="masterAddress">account master address, will use as account</param>
        /// <param name="kdf">Key Derivation Function</param>
        /// <returns>generated wallet</returns>
        public static XrplWallet FromNormalizedText(
            string text,
            string? salt = null,
            bool caseInsensitive = true,
            string algorithm = null,
            string masterAddress = null,
            TextWalletKdf kdf = TextWalletKdf.Sha256)
        {
            var normalized = NormalizeText(text, caseInsensitive);

            var seedBytes = kdf switch
            {
                TextWalletKdf.Sha256 => DeriveSeedWithSha256(normalized, salt),
                TextWalletKdf.Pbkdf2 => DeriveSeedWithPbkdf2(normalized, salt),
                _ => throw new ArgumentOutOfRangeException(nameof(kdf), kdf, "Unsupported KDF")
            };

            return XrplWallet.FromEntropy(seedBytes, masterAddress, algorithm ?? XrplWallet.DEFAULT_ALGORITHM);
        }
        private static byte[] DeriveSeedWithSha256(string text, string? salt, int seedLength = 16)
        {
            if (!string.IsNullOrWhiteSpace(salt))
                text += "::" + salt.Trim();

            var entropy = SHA256.HashData(Encoding.UTF8.GetBytes(text));

            return entropy.Take(seedLength).ToArray(); // 16 байт = 128 бит
        }
        private static byte[] DeriveSeedWithPbkdf2(
            string normalized,
            string? salt,
            int iterations = 100_000,
            int seedLength = 16)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(normalized);

            byte[] saltBytes = null;
            if (!string.IsNullOrWhiteSpace(salt))
            {
                // salt as is, but with Trim
                saltBytes = Encoding.UTF8.GetBytes(salt.Trim());
            }

            using var pbkdf2 = new Rfc2898DeriveBytes(
                passwordBytes,
                saltBytes ?? [],
                iterations,
                HashAlgorithmName.SHA256);

            return pbkdf2.GetBytes(seedLength); // 16 bytes of entropy for seed
        }

        private static string NormalizeText(string input, bool caseInsensitive)
        {
            // We remove extra spaces, convert to lowercase, and normalize characters.
            var normalized = input
                .Trim()
                .Replace("\r\n", "\n") // Windows → Unix
                .Replace("\r", "\n");    // Mac → Unix
            if (caseInsensitive)
            {
                normalized = normalized    // Mac → Unix
                    .ToLowerInvariant();     // if it's important to be case-insensitive
            }
            // Compressing multiple spaces and line breaks into a single space
            normalized = string.Join(" ", normalized
                .Split([' ', '\n', '\t',], StringSplitOptions.RemoveEmptyEntries));

            return normalized;
        }


        /// <summary>
        /// Signs a transaction offline.
        /// </summary>
        /// <param name="transaction">A transaction to be signed offline.</param>
        /// <param name="multisign">Specify true/false to use multisign or actual address (classic/x-address) to make multisign tx request.</param>
        /// <param name="signingFor"></param>
        /// <returns>A Wallet derived from the seed.</returns>
        public SignatureResult Sign(Dictionary<string, object> transaction, bool multisign = false, string? signingFor = null)
        {
            // 1) специальный кейс Batch inner-part
            if (string.Equals($"{transaction[nameof(ITransactionCommon.TransactionType)]}", "Batch", StringComparison.OrdinalIgnoreCase))
            {
                var accounts = transaction.GetBatchSignerAccounts();
                var myAccount = signingFor ?? this.ClassicAddress;
                if (!myAccount.Equals(accounts.Root, StringComparison.OrdinalIgnoreCase)
                    && accounts.Raw.Contains(myAccount, StringComparer.OrdinalIgnoreCase))
                {
                    return SignAsBatchPart(transaction, multisign, signingFor);
                }

                if (!multisign)
                {
                    VerifyBatchSubmitter(transaction, signingFor, true);
                }
            }
            string multisignAddress = "";
            if (multisign)
            {
                // Адрес ПОДПИСАНТА (не владельца!). Если пришёл X-адрес — конвертируем.
                var signerAccount = NormalizeClassic(signingFor);
                return SignMulti(transaction, signerAccount);
            }
            else
            {
                Dictionary<string, object> tx = transaction;

                if (tx.ContainsKey("TxnSignature") || tx.ContainsKey("Signers"))
                {
                    throw new ValidationException("txJSON must not contain `TxnSignature` or `Signers` properties");
                }

                JsonObject txToSignAndEncode = JsonNode.Parse(JsonSerializer.Serialize(transaction, XrplJsonOptions.Default))?.AsObject();
                txToSignAndEncode["SigningPubKey"] = multisignAddress != "" ? "" : this.PublicKey;

                string signature = ComputeSignature(JsonSerializer.Deserialize<Dictionary<string, object>>(txToSignAndEncode.ToJsonString(), XrplJsonOptions.Default), this.PrivateKey);
                txToSignAndEncode["TxnSignature"] = signature;

                string serialized = XrplBinaryCodec.Encode(txToSignAndEncode);
                //this.checkTxSerialization(serialized, tx);
                return new SignatureResult(serialized, HashLedger.HashSignedTx(serialized));
            }
        }

        private string NormalizeClassic(string? signingFor)
        {
            string signerAccount = signingFor ?? this.ClassicAddress;
            return SignerUtilities.NormalizeClassicAddress(signerAccount);
        }


        private SignatureResult SignMulti(Dictionary<string, object> transaction, string signerAccount)
        {
            // txBase — то, что в итоге отправим (накапливает Signers)
            var txBase = JsonNode.Parse(JsonSerializer.Serialize(transaction, XrplJsonOptions.Default))?.AsObject();

            // txForSign — копия для preimage: без Signers и TxnSignature
            var txForSign = txBase.DeepClone().AsObject();
            txForSign["SigningPubKey"] = "";
            txForSign.Remove("TxnSignature");
            txForSign.Remove("Signers");

            string preimageHex = XrplBinaryCodec.EncodeForMultiSigning(txForSign, signerAccount);
            var preimage = Xrpl.AddressCodec.Utils.FromHexToBytes(preimageHex);

            string sig = Xrpl.Keypairs.XrplKeypairs.Sign(preimage, this.PrivateKey);

            var existingSigners = txBase["Signers"] as JsonArray;
            var signers = existingSigners != null
                ? JsonNode.Parse(existingSigners.ToJsonString())?.AsArray() ?? new JsonArray()
                : new JsonArray();
            signers.Add(new JsonObject
            {
                ["Signer"] = new JsonObject
                {
                    ["Account"] = signerAccount,
                    ["SigningPubKey"] = this.PublicKey,
                    ["TxnSignature"] = sig
                }
            });

            // КРИТИЧЕСКОЕ: сортировка Signers по Account
            signers = new JsonArray(
                signers.Select(s => s?.DeepClone()).OrderBy(s =>
                {
                    var acc = s?["Signer"]?["Account"]?.GetValue<string>() ?? "";
                    // для строгого соответствия спекам — сортируем по байтам адреса
                    var accBytes = Xrpl.AddressCodec.XrplCodec.DecodeAccountID(acc);
                    return BitConverter.ToString(accBytes);
                }).ToArray()
            );

            txBase["Signers"] = signers;
            txBase["SigningPubKey"] = "";
            txBase.Remove("TxnSignature");

            string blob = XrplBinaryCodec.Encode(txBase);
            return new SignatureResult(blob, HashLedger.HashSignedTx(blob));
        }

        public SignatureResult SignAsBatchPart(IBatch transaction, bool multisign, string? signingFor)
        {
            var json = transaction.ToJson();
            var tx = JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default)
                         ?? throw new ValidationException("Failed to deserialize tx json");
            return SignAsBatchPart(tx, multisign, signingFor);
        }
        public SignatureResult SignAsBatchPart(Dictionary<string, object> transaction, bool multisign, string? signingFor)
        {
            VerifyBatchSubmitter(transaction, signingFor, false);

            // 1) Стандартизируем вход в JsonObject
            var outer = JsonNode.Parse(JsonSerializer.Serialize(transaction, XrplJsonOptions.Default))?.AsObject()
                ?? throw new ArgumentException("tx is null");

            // 2) Базовые проверки "Batch"
            var txType = outer["TransactionType"]?.GetValue<string>();
            if (!string.Equals(txType, "Batch", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("TransactionType must be 'Batch'.");

            var innerTransactions = outer["RawTransactions"]?.AsArray()
                ?? throw new ValidationException("Batch transaction must have RawTransactions (array).");

            if (innerTransactions.Count == 0 || innerTransactions.Count > 8)
                throw new ValidationException("Batch.RawTransactions length must be between 1 and 8.");

            var normalizedInners = new List<JsonObject>(innerTransactions.Count);
            // 3) Пройдём по внутренним транзакциям и провалидируем по XLS-56
            foreach (var item in innerTransactions.Where(n => n is JsonObject).Select(n => n!.AsObject()))
            {
                var innerTx = item["RawTransaction"]?.AsObject()
                              ?? throw new ValidationException("RawTransaction must be an object.");
                // TransactionType обязателен и не Batch
                var innerType = innerTx["TransactionType"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(innerType))
                    throw new ValidationException("Inner RawTransaction.TransactionType is required.");
                if (string.Equals(innerType, "Batch", StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("Nested Batch is not allowed.");

                // Запрещённые поля
                if (innerTx["TxnSignature"] != null || innerTx["Signers"] != null || innerTx["LastLedgerSequence"] != null)
                    throw new ValidationException("Inner tx must NOT contain TxnSignature, Signers or LastLedgerSequence.");

                // Fee (если присутствует) — ровно "0"
                if (innerTx["Fee"] != null && innerTx["Fee"]?.GetValue<string>() != "0")
                    throw new ValidationException("Inner tx Fee must be string \"0\" when present.");

                // SigningPubKey (если присутствует) — ровно ""
                if (innerTx["SigningPubKey"] != null && innerTx["SigningPubKey"]?.GetValue<string>() != "")
                    throw new ValidationException("Inner tx SigningPubKey must be empty string when present.");

                // Нормализуем под расчёт txid (Fee=\"0\", SigningPubKey=\"\", + tfInnerBatchTxn)
                normalizedInners.Add(innerTx.NormalizeInnerTransaction());
            }


            // 4) Считаем txIDs нормализованных внутренних
            var txIds = normalizedInners.Select(BatchNormalizer.ComputeInnerTxId).ToList();


            // 5) Флаги внешнего батча
            uint flags = 0;
            var fTok = outer["Flags"];
            if (fTok != null)
            {
                if (fTok is JsonValue fVal && fVal.TryGetValue<long>(out var fLong)) flags = (uint)fLong;
                else if (fTok is JsonValue fStr && fStr.TryGetValue<string>(out var fStrVal) && uint.TryParse(fStrVal, out var u)) flags = u;
                outer["Flags"] = flags;
            }

            // NetworkID (если присутствует)
            uint? networkId = null;
            var nTok = outer["NetworkID"];
            if (nTok != null)
            {
                if (nTok is JsonValue nVal && nVal.TryGetValue<long>(out var nLong)) networkId = (uint)nLong;
                else if (nTok is JsonValue nStr && nStr.TryGetValue<string>(out var nStrVal) && uint.TryParse(nStrVal, out var n)) networkId = n;
            }

            // 6) Подписание (оба режима строят один и тот же batch-preimage)
            // batch-preimage = BCH\0 [ + NetworkID ] || Flags || Count || txID[0..N-1]
            byte[] preimage = XrplBinaryCodec.EncodeForSigningBatch(flags, txIds, networkId);
            if (!multisign)
            {
                // MULTI-ACCOUNT: кладём подпись участника в BatchSigners над batch-preimage.
                string signature = XrplKeypairs.Sign(preimage, this.PrivateKey);

                var accountFor = NormalizeClassic(signingFor);

                var existingBatchSigners = outer["BatchSigners"] as JsonArray;
                var batchSigners = existingBatchSigners != null
                    ? JsonNode.Parse(existingBatchSigners.ToJsonString())?.AsArray() ?? new JsonArray()
                    : new JsonArray();

                var signerObj = new JsonObject
                {
                    ["Account"] = accountFor,
                    ["SigningPubKey"] = this.PublicKey,
                    ["TxnSignature"] = signature
                    // Если нужен мультисиг под ЭТИМ ЖЕ аккаунтом — вместо пары выше положи "Signers": [ { Signer{Account,SigningPubKey,TxnSignature} }, ... ]
                    // Подпись каждого Signer — над тем же preimage.
                };
                batchSigners.Add(new JsonObject { ["BatchSigner"] = signerObj });

                // Сортировка BatchSigners и вложенных Signers по account-id (как в XRPL)
                outer["BatchSigners"] = BatchSigningHelper.SortBatchSigners(batchSigners);

                // Для внешнего Batch при наличии BatchSigners: пустой SigningPubKey и БЕЗ TxnSignature
                //outer["SigningPubKey"] = "";
                //outer.Remove("TxnSignature");
            }
            else
            {
                // === MULTI-SIG под одним BatchSigner.Account через Signers[] ===

                if (string.IsNullOrWhiteSpace(signingFor))
                {
                    throw new ValidationException("Batch inner multisign requires signingFor = owner account (RawTransaction.Account).");
                }

                var ownerAccount = Xrpl.AddressCodec.XrplCodec.IsValidClassicAddress(signingFor)
                    ? signingFor
                    : XrplAddressCodec.XAddressToClassicAddress(signingFor).ClassicAddress;

                // Для inner multisign (BatchSigner.Signers[]) по XLS-56:
                // preimage = batch-preimage + signer's account ID bytes
                // (batch-preimage уже содержит HashPrefix.Batch, дополнительный TransactionMultiSig не нужен)
                var signerAccountId = Xrpl.AddressCodec.XrplCodec.DecodeAccountID(this.ClassicAddress);
                var fullPreimage = new byte[preimage.Length + signerAccountId.Length];
                Buffer.BlockCopy(preimage, 0, fullPreimage, 0, preimage.Length);
                Buffer.BlockCopy(signerAccountId, 0, fullPreimage, preimage.Length, signerAccountId.Length);

                var sig = Xrpl.Keypairs.XrplKeypairs.Sign(fullPreimage, this.PrivateKey);

                // Достаём/создаём BatchSigner для ownerAccount
                var existingBatchSigners = outer["BatchSigners"] as JsonArray;
                var batchSigners = existingBatchSigners != null
                    ? JsonNode.Parse(existingBatchSigners.ToJsonString())?.AsArray() ?? new JsonArray()
                    : new JsonArray();
                var bs = BatchSigningHelper.FindOrCreateBatchSigner(batchSigners, ownerAccount);

                // Переводим (если нужно) single-форму в мультисиг-форму
                if (bs["Signers"] == null)
                {
                    bs.Remove("SigningPubKey");
                    bs.Remove("TxnSignature");
                    bs["Signers"] = new JsonArray();
                }

                // Добавляем текущего подписанта
                var signersArr = bs["Signers"]!.AsArray();
                var signerEntry = new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = this.ClassicAddress,   // именно аккаунт ПОДПИСАНТА (из локального кошелька)
                        ["SigningPubKey"] = this.PublicKey,
                        ["TxnSignature"] = sig
                    }
                };

                // Защита от дублей (по тройке Account|SigningPubKey|TxnSignature)
                static string KeyOf(JsonObject se)
                {
                    var so = se["Signer"]!.AsObject();
                    return $"{so["Account"]?.GetValue<string>()}|{so["SigningPubKey"]?.GetValue<string>()}|{so["TxnSignature"]?.GetValue<string>()}";
                }
                var seen = new HashSet<string>(
                    signersArr.Where(n => n is JsonObject).Select(n => KeyOf(n!.AsObject())),
                    StringComparer.Ordinal);
                if (seen.Add(KeyOf(signerEntry)))
                    signersArr.Add(signerEntry);

                // Каноническая сортировка и Signers, и BatchSigners
                outer["BatchSigners"] = BatchSigningHelper.SortBatchSigners(batchSigners);

                // Корень без подписи
                //outer["SigningPubKey"] = "";
                //outer.Remove("TxnSignature");
            }
            // 9) Сериализация и хэш
            string signedHex = XrplBinaryCodec.Encode(outer);
            string txHash = HashLedger.HashSignedTx(signedHex);
            var txRes = XrplBinaryCodec.Decode(signedHex);

            return new SignatureResult(signedHex, txHash);
        }

        private void VerifyBatchSubmitter(Dictionary<string, object> transaction, string? signingFor, bool allowRoot)
        {
            var status = transaction.GetBatchSignStatus();
            var me = NormalizeClassic(signingFor);

            // 3. Проверяем: должен ли этот аккаунт подписывать?
            bool isRoot = status.Root.Equals(me, StringComparison.OrdinalIgnoreCase);
            bool isInner = status.InnerRequired.Contains(me, StringComparer.OrdinalIgnoreCase);

            if (isRoot && !allowRoot)
            {
                // Мой аккаунт не является одним из владельцев Batch/RawTransactions
                throw new UnauthorizedAccessException($"root account must submit top level of this batch tx");
            }
            if (!isInner && !isRoot)
            {
                // Мой аккаунт не является одним из владельцев Batch/RawTransactions
                throw new UnauthorizedAccessException($"{me} account has no access to submit this batch tx");
            }
            if (isInner)
            {
                // Если аккаунт-ВЛАДЕЛЕЦ inner уже "подписан", это НЕ значит что нельзя добавить еще одного мультиподписанта.
                // Запрещаем только повтор одного и того же signer'а (this.ClassicAddress) для этого owner'а.
                if (!status.InnerMissing.Contains(me))
                {
                    try
                    {
                        var outer = JsonNode.Parse(JsonSerializer.Serialize(transaction, XrplJsonOptions.Default))?.AsObject();
                        var batchSigners = outer?["BatchSigners"] as JsonArray;

                        if (batchSigners != null)
                        {
                            // Найдем BatchSigner для owner = me
                            foreach (var w in batchSigners.Where(n => n is JsonObject).Select(n => n!.AsObject()))
                            {
                                var bs = w["BatchSigner"]?.AsObject() ?? w;
                                var acc = bs["Account"]?.GetValue<string>();
                                if (!string.Equals(acc, me, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var signersArr = bs["Signers"] as JsonArray;
                                if (signersArr == null)
                                    break; // single-sig BatchSigner: повтор запрещаем

                                // Проверяем, не добавлял ли этот signer уже подпись для данного owner
                                var signerMe = NormalizeClassic(this.ClassicAddress);
                                var already = signersArr
                                    .Where(n => n is JsonObject).Select(n => n!.AsObject())
                                    .Select(x => x["Signer"]?["Account"]?.GetValue<string>())
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Any(x => string.Equals(NormalizeClassic(x!), signerMe, StringComparison.OrdinalIgnoreCase));

                                if (!already)
                                {
                                    // owner уже имеет BatchSigner, но этот signer ещё не участвовал — разрешаем продолжить
                                    return;
                                }

                                // этот signer уже подписывал для owner
                                throw new UnauthorizedAccessException($"{me} account already submit this batch tx");
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        throw;
                    }
                    catch
                    {
                        // если вдруг JSON кривой — лучше зафейлиться как раньше
                    }

                    // Старое поведение (если не смогли доказать, что это просто второй мультиподписант)
                    throw new UnauthorizedAccessException($"{me} account already submit this batch tx");
                }
            }

        }

        /// <summary>
        /// Signs a transaction offline.
        /// </summary>
        /// <param name="tx">A transaction to be signed offline.</param>
        /// <param name="multisign">Specify true/false to use multisign or actual address (classic/x-address) to make multisign tx request.</param>
        /// <param name="signingFor"></param>
        /// <returns>A Wallet derived from the seed.</returns>
        public SignatureResult Sign(ITransactionRequest tx, bool multisign = false, string? signingFor = null)
        {
            Dictionary<string, object> txJson = JsonSerializer.Deserialize<Dictionary<string, object>>(tx.ToJson(), XrplJsonOptions.Default);
            return Sign(txJson, multisign, signingFor);
        }

        /// <summary>
        /// Verifies a signed transaction offline.
        /// </summary>
        /// <param name="signedTransaction">A signed transaction (hex string of signTransaction result) to be verified offline.</param>
        /// <returns>Returns true if a signedTransaction is valid.</returns>
        public bool VerifyTransaction(string signedTransaction)
        {
            JsonNode txNode = XrplBinaryCodec.Decode(signedTransaction);
            Dictionary<string, object> txDict = JsonSerializer.Deserialize<Dictionary<string, object>>(txNode.ToJsonString(), XrplJsonOptions.Default);
            string messageHex = XrplBinaryCodec.EncodeForSigning(txDict);
            string signature = txNode["TxnSignature"]?.GetValue<string>();
            return XrplKeypairs.Verify(messageHex.FromHex(), signature, this.PublicKey);
        }

        public string GetXAddress(uint tag, bool isTestnet = false)
        {
            return XrplAddressCodec.ClassicAddressToXAddress(this.ClassicAddress, tag, isTestnet);
        }

        public string ComputeSignature(Dictionary<string, object> transaction, string privateKey, string? signAs = null)
        {
            string encoded = XrplBinaryCodec.EncodeForSigning(transaction);
            return XrplKeypairs.Sign(AddressCodec.Utils.FromHexToBytes(encoded), privateKey);
        }
        /// <summary>
        /// Объединяет несколько частично подписанных Batch-транзакций (txBlob в hex) в один финальный blob.
        /// Условия:
        ///  - Все входные blob'ы должны быть Batch и иметь ИДЕНТИЧНОЕ тело (кроме SigningPubKey/TxnSignature/BatchSigners).
        ///  - Объединяются только подписи в BatchSigners (и при отсутствии BatchSigners — внешняя подпись).
        ///  - BatchSigners сортируются по Account; вложенные Signers — по Signer.Account.
        /// </summary>
        public static SignatureResult CombineBatchSigners(params string[] txBlobs)
        {
            if (txBlobs == null || txBlobs.Length == 0)
                throw new ArgumentException("No tx blobs provided.");
            if (txBlobs.Length == 1)
            {
                var single = txBlobs[0];
                return new SignatureResult(single, HashLedger.HashSignedTx(single));
            }

            // Канонизация тела: выкидываем *все* подписи (outer + inner + multisign)
            static JsonObject Canonicalize(JsonObject x)
            {
                var c = x.DeepClone().AsObject();
                c.Remove("TxnSignature");
                c.Remove("SigningPubKey");
                c.Remove("BatchSigners");
                c.Remove("Signers");
                return c;
            }

            // ---------- 1) decode + sanity ----------

            var decoded = txBlobs.Select(DecodeToObject).ToList();
            foreach (var o in decoded)
            {
                var tt = o["TransactionType"]?.GetValue<string>();
                if (!string.Equals(tt, "Batch", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("All blobs must be Batch transactions.");
            }

            // ---------- 2) проверяем, что тела идентичны (без подписей) ----------

            var baseCanon = Canonicalize(decoded[0]);
            for (int i = 1; i < decoded.Count; i++)
            {
                if (!JsonNode.DeepEquals(baseCanon, Canonicalize(decoded[i])))
                    throw new InvalidOperationException("Incompatible Batch bodies. All inputs must have identical non-signing fields.");
            }

            // ---------- 3) base для результата ----------

            var combined = decoded[0].DeepClone().AsObject();
            combined.Remove("BatchSigners");
            combined.Remove("Signers");
            combined.Remove("TxnSignature");
            combined.Remove("SigningPubKey");

            // ---------- 4) собираем и мержим BatchSigners (inner-подписи) ----------

            var byAccount = new Dictionary<string, JsonObject>(StringComparer.Ordinal); // Account -> BatchSigner object

            foreach (var outer in decoded)
            {
                var arr = outer["BatchSigners"] as JsonArray;
                if (arr == null) continue;

                foreach (var w in arr.Where(n => n is JsonObject).Select(n => n!.AsObject()))
                {
                    var bs = w["BatchSigner"]?.AsObject() ?? w;
                    var accRaw = bs["Account"]?.GetValue<string>() ?? throw new InvalidOperationException("BatchSigner missing Account.");

                    var acc = SignerUtilities.NormalizeClassicAddress(accRaw);
                    bs["Account"] = acc; // нормализуем

                    if (!byAccount.TryGetValue(acc, out var existing))
                    {
                        byAccount[acc] = bs.DeepClone().AsObject();
                    }
                    else
                    {
                        // Уже есть BatchSigner по этому аккаунту → мержим
                        BatchSigningHelper.MergeBatchSigner(existing, bs);
                    }
                }
            }

            // Внутри каждого BatchSigner тоже может быть multisign (Signers[])
            // Делаем dedupe + сортировку по AccountID для внутренних Signers
            foreach (var kvp in byAccount.ToList())
            {
                var bs = kvp.Value;
                var signersArr = bs["Signers"] as JsonArray;
                if (signersArr == null || signersArr.Count == 0)
                    continue;

                bs["Signers"] = SignerUtilities.DedupeAndSortSigners(signersArr);
            }

            // Собираем в массив-обёртку
            var mergedBatchSignersArr = new JsonArray(byAccount.Values.Select(v => (JsonNode)new JsonObject { ["BatchSigner"] = v }).ToArray());
            combined["BatchSigners"] = BatchSigningHelper.SortBatchSigners(mergedBatchSignersArr);

            // ---------- 5) собираем и мержим root Signers (top multisign) ----------

            var allRootSigners = new List<JsonNode>();

            foreach (var outer in decoded)
            {
                if (outer["Signers"] is not JsonArray arr) continue;
                foreach (var it in arr)
                {
                    if (it is JsonObject itObj)
                        allRootSigners.Add(itObj.DeepClone());
                }
            }

            if (allRootSigners.Count > 0)
            {
                // dedupe and sort root Signers using helper
                var sortedRootSigners = SignerUtilities.DedupeAndSortSigners(new JsonArray(allRootSigners.ToArray()));
                combined["Signers"] = sortedRootSigners;

                // XRPL-правило для multisign: SigningPubKey = "", TxnSignature отсутствует
                combined["SigningPubKey"] = "";
                combined.Remove("TxnSignature");
            }
            else
            {
                // 6) Внешняя подпись: если во всех blob'ах одинаковая — сохраняем её,
                // независимо от наличия BatchSigners.
                string? outSig = null, outPub = null;
                bool gotOuter = false;

                foreach (var o in decoded)
                {
                    var s = o["TxnSignature"]?.GetValue<string>();
                    var p = o["SigningPubKey"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(s) && string.IsNullOrEmpty(p))
                        continue;

                    if (!gotOuter)
                    {
                        gotOuter = true;
                        outSig = s;
                        outPub = p;
                        continue;
                    }

                    if (!string.Equals(outSig, s, StringComparison.Ordinal) ||
                        !string.Equals(outPub, p, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Conflicting outer signatures across inputs.");
                    }
                }

                if (gotOuter)
                {
                    if (!string.IsNullOrEmpty(outPub))
                        combined["SigningPubKey"] = outPub!;
                    if (!string.IsNullOrEmpty(outSig))
                        combined["TxnSignature"] = outSig!;
                }
                else
                {
                    combined.Remove("SigningPubKey");
                    combined.Remove("TxnSignature");
                }
            }

            // ---------- 7) encode + hash ----------

            string signedHex = XrplBinaryCodec.Encode(combined);
            string txHash = HashLedger.HashSignedTx(signedHex);
            return new SignatureResult(signedHex, txHash);
        }

        /// <summary>Декодирует hex blob в JsonObject.</summary>
        private static JsonObject DecodeToObject(string blobHex)
        {
            JsonNode dec = XrplBinaryCodec.Decode(blobHex);
            return dec.AsObject();
        }
    }
}