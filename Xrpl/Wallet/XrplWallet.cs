using NBitcoin;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Keypairs;
using Xrpl.Models;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Utils.Hashes;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/Wallet/index.ts

namespace Xrpl.Wallet
{
    public class SignatureResult
    {
        [JsonProperty(propertyName: "tx_blob")]
        public string TxBlob { get; set; }
        public string Hash { get; set; }

        public SignatureResult(string txBlob, string hash)
        {
            TxBlob = txBlob;
            Hash = hash;
        }

        public Dictionary<string, dynamic> GetTxDictionary()
        {
            if (TxBlob == null)
            {
                throw new NullReferenceException(nameof(TxBlob));
            }
            return JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(
                XrplBinaryCodec.Decode(TxBlob).ToString());
        }

        public ITransactionRequest GetTx()
        {
            if (TxBlob == null)
            {
                throw new NullReferenceException(nameof(TxBlob));
            }
            return JsonConvert.DeserializeObject<TransactionRequest>(
                XrplBinaryCodec.Decode(TxBlob).ToString());
        }
    }
    public enum TextWalletKdf
    {
        Sha256 = 0,
        Pbkdf2 = 1,
    }

    public class XrplWallet
    {

        public static string DEFAULT_ALGORITHM = "ed25519";

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
        public static XrplWallet Generate(string algorithm = "ed25519")
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

        /// <summary>
        /// Creates a Wallet from xumm numbers.
        /// </summary>
        /// <returns>A Wallet from xumm numbers.</returns>
        public static XrplWallet FromXummNumbers(string[] numbers)
        {
            byte[] entropy = XummExtension.EntropyFromXummNumbers(numbers);
            return FromEntropy(entropy);
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
            var encodeAlgorithm = algorithm == "ed25519" ? "ed25519" : "secp256k1";
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
        public static XrplWallet FromXummNumbers(string[] numbers, string algorithm = "secp256k1")
        {
            byte[] entropy = XummExtension.EntropyFromXummNumbers(numbers);
            return FromEntropy(entropy, null, algorithm);
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
        public SignatureResult Sign(Dictionary<string, dynamic> transaction, bool multisign = false, string? signingFor = null)
        {
            // 1) специальный кейс Batch inner-part
            if (transaction[nameof(ITransactionCommon.TransactionType)] == "Batch")
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
                Dictionary<string, dynamic> tx = transaction;

                if (tx.ContainsKey("TxnSignature") || tx.ContainsKey("Signers"))
                {
                    throw new ValidationException("txJSON must not contain `TxnSignature` or `Signers` properties");
                }

                JObject txToSignAndEncode = JToken.FromObject(transaction).ToObject<JObject>();
                txToSignAndEncode["SigningPubKey"] = multisignAddress != "" ? "" : this.PublicKey;

                string signature = ComputeSignature(txToSignAndEncode.ToObject<Dictionary<string, dynamic>>(), this.PrivateKey);
                txToSignAndEncode.Add("TxnSignature", signature);

                string serialized = XrplBinaryCodec.Encode(txToSignAndEncode);
                //this.checkTxSerialization(serialized, tx);
                return new SignatureResult(serialized, HashLedger.HashSignedTx(serialized));
            }
        }

        private string NormalizeClassic(string? signingFor)
        {
            string signerAccount = signingFor ?? this.ClassicAddress;
            if (!Xrpl.AddressCodec.XrplCodec.IsValidClassicAddress(signerAccount))
            {
                var x = XrplAddressCodec.XAddressToClassicAddress(signerAccount);
                signerAccount = x.ClassicAddress;
            }

            return signerAccount;
        }


        private SignatureResult SignMulti(Dictionary<string, dynamic> transaction, string signerAccount)
        {
            // txBase — то, что в итоге отправим (накапливает Signers)
            var txBase = JObject.FromObject(transaction);

            // txForSign — копия для preimage: без Signers и TxnSignature
            var txForSign = (JObject)txBase.DeepClone();
            txForSign["SigningPubKey"] = "";
            txForSign.Remove("TxnSignature");
            txForSign.Remove("Signers");

            string preimageHex = XrplBinaryCodec.EncodeForMultiSigning(txForSign, signerAccount);
            var preimage = Xrpl.AddressCodec.Utils.FromHexToBytes(preimageHex);

            string sig = Xrpl.Keypairs.XrplKeypairs.Sign(preimage, this.PrivateKey);

            var signers = (txBase["Signers"] as JArray) ?? new JArray();
            signers.Add(new JObject
            {
                ["Signer"] = new JObject
                {
                    ["Account"] = signerAccount,
                    ["SigningPubKey"] = this.PublicKey,
                    ["TxnSignature"] = sig
                }
            });

            // КРИТИЧЕСКОЕ: сортировка Signers по Account
            signers = new JArray(
                signers.OrderBy(s =>
                {
                    var acc = (string?)s["Signer"]?["Account"] ?? "";
                    // для строгого соответствия спекам — сортируем по байтам адреса
                    var accBytes = Xrpl.AddressCodec.XrplCodec.DecodeAccountID(acc);
                    return BitConverter.ToString(accBytes);
                })
            );

            txBase["Signers"] = signers;
            txBase["SigningPubKey"] = "";
            txBase.Remove("TxnSignature");

            string blob = XrplBinaryCodec.Encode(txBase);
            return new SignatureResult(blob, HashLedger.HashSignedTx(blob));
        }

        public static string CombineMultiSigners(params string[] txBlobs)
        {
            if (txBlobs == null || txBlobs.Length == 0)
                throw new ArgumentException("No transactions to combine.");

            JObject Decode(string hex)
            {
                var dec = Xrpl.BinaryCodec.XrplBinaryCodec.Decode(hex);
                return dec is JObject jo ? jo
                     : JObject.FromObject(dec!);
            }

            // X->classic
            static string ToClassic(string addr)
            {
                if (Xrpl.AddressCodec.XrplCodec.IsValidClassicAddress(addr)) return addr;
                var x = XrplAddressCodec.XAddressToClassicAddress(addr);
                return x.ClassicAddress;
            }
            static byte[] AID(string acc) => Xrpl.AddressCodec.XrplCodec.DecodeAccountID(ToClassic(acc));

            var objs = txBlobs.Select(Decode).ToArray();

            // Запрет single-sig: у корня не должно быть ни TxnSignature, ни непустого SigningPubKey
            foreach (var o in objs)
            {
                if (o["TxnSignature"] != null)
                    throw new InvalidOperationException("Not a forMultisign tx: has TxnSignature.");
                if ((string?)o["SigningPubKey"] is string spk && spk.Length != 0)
                    throw new InvalidOperationException("Not a forMultisign tx: SigningPubKey must be empty.");
            }

            // Канонизация тела для сравнения
            JObject Canon(JObject o)
            {
                var c = (JObject)o.DeepClone();
                c.Remove("TxnSignature");
                c.Remove("SigningPubKey");
                c.Remove("Signers");
                // Нормализуем Account (поддержка X-адресов)
                if (c["Account"] is JValue v && v.Type == JTokenType.String)
                    c["Account"] = ToClassic((string)v);
                return c;
            }

            var canon0 = Canon(objs[0]);
            if (objs.Skip(1).Any(o => !JToken.DeepEquals(canon0, Canon(o))))
                throw new InvalidOperationException("Different tx bodies; cannot combine.");

            // Собираем всех подписантов
            var all = new List<JObject>();
            foreach (var o in objs)
            {
                if (o["Signers"] is JArray arr)
                    foreach (var it in arr.Children<JObject>())
                        all.Add((JObject)it.DeepClone());
            }
            if (all.Count == 0)
                throw new InvalidOperationException("No Signers found.");

            // 🔑 Убираем дубли по (Account, SigningPubKey, TxnSignature)
            var unique = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var j in all)
            {
                var so = j["Signer"] as JObject;
                if (so == null) continue;

                var acc = (string?)so["Account"] ?? "";
                var pk = (string?)so["SigningPubKey"] ?? "";
                var sig = (string?)so["TxnSignature"] ?? "";

                var key = $"{acc}|{pk}|{sig}";
                if (!unique.ContainsKey(key))
                    unique[key] = j;
            }

            // Сортировка по бинарному AccountID уже по unique.Values
            var sorted = unique.Values
                .OrderBy(j =>
                {
                    var acc = (string?)j["Signer"]?["Account"] ?? "";
                    return AID(acc);
                }, Comparer<byte[]>.Create((a, b) =>
                {
                    for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                    {
                        int d = a[i].CompareTo(b[i]);
                        if (d != 0) return d;
                    }
                    return a.Length.CompareTo(b.Length);
                }))
                .ToArray();

            var outTx = (JObject)objs[0].DeepClone();
            outTx["Signers"] = new JArray(sorted);
            outTx["SigningPubKey"] = "";   // как требует мультиподпись
            outTx.Remove("TxnSignature");

            return Xrpl.BinaryCodec.XrplBinaryCodec.Encode(outTx);
        }

        public SignatureResult SignAsBatchPart(IBatch transaction, bool multisign, string? signingFor)
        {
            var json = transaction.ToJson();
            var tx = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json)
                         ?? throw new ValidationException("Failed to deserialize tx json");
            return SignAsBatchPart(tx, multisign, signingFor);
        }
        public SignatureResult SignAsBatchPart(Dictionary<string, dynamic> transaction, bool multisign, string? signingFor)
        {
            VerifyBatchSubmitter(transaction, signingFor, false);

            // 1) Стандартизируем вход в JObject
            var outer = JObject.FromObject(transaction) ?? throw new ArgumentException("tx is null");

            // 2) Базовые проверки "Batch"
            var txType = outer.Value<string>("TransactionType");
            if (!string.Equals(txType, "Batch", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("TransactionType must be 'Batch'.");

            var innerTransactions = outer["RawTransactions"] as JArray
                ?? throw new ValidationException("Batch transaction must have RawTransactions (array).");

            if (innerTransactions.Count == 0 || innerTransactions.Count > 8)
                throw new ValidationException("Batch.RawTransactions length must be between 1 and 8.");

            var normalizedInners = new List<JObject>(innerTransactions.Count);
            // 3) Пройдём по внутренним транзакциям и провалидируем по XLS-56
            foreach (var item in innerTransactions.Children<JObject>())
            {
                var innerTx = item["RawTransaction"] as JObject
                              ?? throw new ValidationException("RawTransaction must be an object.");
                // TransactionType обязателен и не Batch
                var innerType = innerTx.Value<string>("TransactionType");
                if (string.IsNullOrWhiteSpace(innerType))
                    throw new ValidationException("Inner RawTransaction.TransactionType is required.");
                if (string.Equals(innerType, "Batch", StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("Nested Batch is not allowed.");

                // Запрещённые поля
                if (innerTx["TxnSignature"] != null || innerTx["Signers"] != null || innerTx["LastLedgerSequence"] != null)
                    throw new ValidationException("Inner tx must NOT contain TxnSignature, Signers or LastLedgerSequence.");

                // Fee (если присутствует) — ровно "0"
                if (innerTx["Fee"] != null && innerTx.Value<string>("Fee") != "0")
                    throw new ValidationException("Inner tx Fee must be string \"0\" when present.");

                // SigningPubKey (если присутствует) — ровно ""
                if (innerTx["SigningPubKey"] != null && innerTx.Value<string>("SigningPubKey") != "")
                    throw new ValidationException("Inner tx SigningPubKey must be empty string when present.");

                // Нормализуем под расчёт txid (Fee=\"0\", SigningPubKey=\"\", + tfInnerBatchTxn)
                normalizedInners.Add(innerTx.NormalizeInnerForBatch());
            }


            // 4) Считаем txIDs нормализованных внутренних
            var txIds = normalizedInners.Select(BatchUtils.ComputeInnerTxId).ToList();


            // 5) Флаги внешнего батча
            uint flags = 0;
            var fTok = outer["Flags"];
            if (fTok != null)
            {
                if (fTok.Type == JTokenType.Integer) flags = (uint)fTok.Value<long>();
                else if (fTok.Type == JTokenType.String && uint.TryParse((string)fTok, out var u)) flags = u;
                outer["Flags"] = flags;
            }

            // NetworkID (если присутствует)
            uint? networkId = null;
            var nTok = outer["NetworkID"];
            if (nTok != null)
            {
                if (nTok.Type == JTokenType.Integer) networkId = (uint)nTok.Value<long>();
                else if (nTok.Type == JTokenType.String && uint.TryParse((string)nTok, out var n)) networkId = n;
            }

            // 6) Подписание (оба режима строят один и тот же batch-preimage)
            // batch-preimage = BCH\0 [ + NetworkID ] || Flags || Count || txID[0..N-1]
            byte[] preimage = XrplBinaryCodec.EncodeForSigningBatch(flags, txIds, networkId);
            if (!multisign)
            {
                // MULTI-ACCOUNT: кладём подпись участника в BatchSigners над batch-preimage.
                string signature = XrplKeypairs.Sign(preimage, this.PrivateKey);

                var accountFor = NormalizeClassic(signingFor);

                var batchSigners = (outer["BatchSigners"] as JArray) ?? new JArray();

                var signerObj = new JObject
                {
                    ["Account"] = accountFor,
                    ["SigningPubKey"] = this.PublicKey,
                    ["TxnSignature"] = signature
                    // Если нужен мультисиг под ЭТИМ ЖЕ аккаунтом — вместо пары выше положи "Signers": [ { Signer{Account,SigningPubKey,TxnSignature} }, ... ]
                    // Подпись каждого Signer — над тем же preimage.
                };
                batchSigners.Add(new JObject { ["BatchSigner"] = signerObj });

                // Сортировка BatchSigners и вложенных Signers по account-id (как в XRPL)
                outer["BatchSigners"] = SortBatchSigners(batchSigners);

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

                // Подпись текущим кошельком НАД batch-preimage (ВАЖНО: не EncodeForMultiSigning)
                var sig = Xrpl.Keypairs.XrplKeypairs.Sign(preimage, this.PrivateKey);

                // Достаём/создаём BatchSigner для ownerAccount
                var batchSigners = (outer["BatchSigners"] as JArray) ?? new JArray();
                var bs = FindOrCreateBatchSigner(batchSigners, ownerAccount);

                // Переводим (если нужно) single-форму в мультисиг-форму
                if (bs["Signers"] == null)
                {
                    bs.Remove("SigningPubKey");
                    bs.Remove("TxnSignature");
                    bs["Signers"] = new JArray();
                }

                // Добавляем текущего подписанта
                var signersArr = (JArray)bs["Signers"]!;
                var signerEntry = new JObject
                {
                    ["Signer"] = new JObject
                    {
                        ["Account"] = this.ClassicAddress,   // именно аккаунт ПОДПИСАНТА (из локального кошелька)
                        ["SigningPubKey"] = this.PublicKey,
                        ["TxnSignature"] = sig
                    }
                };

                // Защита от дублей (по тройке Account|SigningPubKey|TxnSignature)
                static string KeyOf(JObject se)
                {
                    var so = (JObject)se["Signer"]!;
                    return $"{(string?)so["Account"]}|{(string?)so["SigningPubKey"]}|{(string?)so["TxnSignature"]}";
                }
                var seen = new HashSet<string>(signersArr.Children<JObject>().Select(KeyOf), StringComparer.Ordinal);
                if (seen.Add(KeyOf(signerEntry)))
                    signersArr.Add(signerEntry);

                // Каноническая сортировка и Signers, и BatchSigners
                outer["BatchSigners"] = SortBatchSigners(batchSigners);

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

        private void VerifyBatchSubmitter(Dictionary<string, dynamic> transaction, string? signingFor, bool allowRoot)
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
                        var outer = JObject.FromObject(transaction);
                        var batchSigners = outer["BatchSigners"] as JArray;

                        if (batchSigners != null)
                        {
                            // Найдем BatchSigner для owner = me
                            foreach (var w in batchSigners.Children<JObject>())
                            {
                                var bs = w["BatchSigner"] as JObject ?? w;
                                var acc = (string?)bs["Account"];
                                if (!string.Equals(acc, me, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var signersArr = bs["Signers"] as JArray;
                                if (signersArr == null)
                                    break; // single-sig BatchSigner: повтор запрещаем

                                // Проверяем, не добавлял ли этот signer уже подпись для данного owner
                                var signerMe = NormalizeClassic(this.ClassicAddress);
                                var already = signersArr.Children<JObject>()
                                    .Select(x => (string?)x["Signer"]?["Account"])
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

        static JObject FindOrCreateBatchSigner(JArray batchSigners, string owner)
        {
            // ищем { BatchSigner: { Account: owner, ... } }
            foreach (var w in batchSigners.Children<JObject>())
            {
                var bs = w["BatchSigner"] as JObject;
                if (bs == null) continue;
                var acc = (string?)bs["Account"];
                if (string.Equals(acc ?? "", owner, StringComparison.Ordinal))
                    return bs;
            }
            var created = new JObject { ["Account"] = owner };
            batchSigners.Add(new JObject { ["BatchSigner"] = created });
            return created;
        }
        /// <summary>
        /// Сортировка BatchSigners по Account (численно по account-id), а также сортировка внутренних Signers.
        /// </summary>
        private static JArray SortBatchSigners(JArray batchSigners)
        {
            if (batchSigners == null || batchSigners.Count == 0) return batchSigners ?? new JArray();

            byte[] Aid(string acc) => Xrpl.AddressCodec.XrplCodec.DecodeAccountID(acc);
            int Cmp(string a, string b)
            {
                var ab = Aid(a); var bb = Aid(b);
                int n = Math.Min(ab.Length, bb.Length);
                for (int i = 0; i < n; i++) { int d = ab[i].CompareTo(bb[i]); if (d != 0) return d; }
                return ab.Length.CompareTo(bb.Length);
            }

            // helper: сортировка массива Signers внутри одного BatchSigner
            void SortInnerSigners(JObject batchSignerObj)
            {
                var signersArr = batchSignerObj["Signers"] as JArray;
                if (signersArr == null) return;
                // Канонизируем Account каждого Signer к classic перед сортировкой
                foreach (var j in signersArr.Children<JObject>())
                {
                    var so = j["Signer"] as JObject;
                    if (so == null) continue;
                    var accRaw = (string?)so["Account"] ?? "";
                    if (!Xrpl.AddressCodec.XrplCodec.IsValidClassicAddress(accRaw))
                        so["Account"] = XrplAddressCodec.XAddressToClassicAddress(accRaw).ClassicAddress;
                }
                var sorted = signersArr
                     .Children<JObject>()
                     .OrderBy(s =>
                     {
                         var acc = (string?)s["Signer"]?["Account"] ?? "";
                         return acc;
                     }, Comparer<string>.Create(Cmp))
                     .Select(s => new JObject { ["Signer"] = s["Signer"] })
                     .ToArray();
                batchSignerObj["Signers"] = new JArray(sorted);
                //var signersArr = batchSignerObj["Signers"] as JArray;
                //if (signersArr == null) return;
                //var sorted = signersArr
                //                    .Children<JObject>()
                //                    .OrderBy(s =>
                //                    {
                //                        var acc = (string?)s["Signer"]?["Account"] ?? "";
                //                        return acc;
                //                    }, Comparer<string>.Create(Cmp))
                //                .Select(s => new JObject { ["Signer"] = s["Signer"] })
                //                .ToArray();
                //batchSignerObj["Signers"] = new JArray(sorted);
            }

            var sortedBatchSigners = batchSigners
                            .Children<JObject>()
                            .Select(o => o["BatchSigner"] as JObject)
                            .Where(o => o != null)
                            .Select(o =>
                            {
                                SortInnerSigners(o!);
                                return o!;
                            })
                            .OrderBy(o => (string?)o["Account"] ?? "", Comparer<string>.Create(Cmp))
                            .Select(o => new JObject { ["BatchSigner"] = o })
                            .ToArray();

            return new JArray(sortedBatchSigners);
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
            Dictionary<string, dynamic> txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(tx.ToJson());
            return Sign(txJson, multisign, signingFor);
        }

        /// <summary>
        /// Verifies a signed transaction offline.
        /// </summary>
        /// <param name="signedTransaction">A signed transaction (hex string of signTransaction result) to be verified offline.</param>
        /// <returns>Returns true if a signedTransaction is valid.</returns>
        public bool VerifyTransaction(string signedTransaction)
        {
            JToken tx = XrplBinaryCodec.Decode(signedTransaction);
            string messageHex = XrplBinaryCodec.EncodeForSigning(tx.ToObject<Dictionary<string, dynamic>>());
            string signature = (string)tx["TxnSignature"];
            return XrplKeypairs.Verify(messageHex.FromHex(), signature, this.PublicKey);
        }

        public string GetXAddress(uint tag, bool isTestnet = false)
        {
            return XrplAddressCodec.ClassicAddressToXAddress(this.ClassicAddress, tag, isTestnet);
        }

        public string ComputeSignature(Dictionary<string, dynamic> transaction, string privateKey, string? signAs = null)
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

            // ---------- helpers ----------

            JObject DecodeToObject(string hex)
            {
                var dec = Xrpl.BinaryCodec.XrplBinaryCodec.Decode(hex);
                return dec is JObject jo ? jo : JObject.FromObject(dec!);
            }

            static string ToClassic(string addr)
            {
                if (Xrpl.AddressCodec.XrplCodec.IsValidClassicAddress(addr))
                    return addr;

                var x = XrplAddressCodec.XAddressToClassicAddress(addr);
                return x.ClassicAddress;
            }

            static byte[] AID(string acc) => Xrpl.AddressCodec.XrplCodec.DecodeAccountID(ToClassic(acc));

            // Канонизация тела: выкидываем *все* подписи (outer + inner + multisign)
            JObject Canonicalize(JObject x)
            {
                var c = (JObject)x.DeepClone();
                c.Remove("TxnSignature");
                c.Remove("SigningPubKey");
                c.Remove("BatchSigners");
                c.Remove("Signers");  // важный момент: Signers не часть "тела"
                return c;
            }

            // ---------- 1) decode + sanity ----------

            var decoded = txBlobs.Select(DecodeToObject).ToList();
            foreach (var o in decoded)
            {
                var tt = (string?)o["TransactionType"];
                if (!string.Equals(tt, "Batch", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("All blobs must be Batch transactions.");
            }

            // ---------- 2) проверяем, что тела идентичны (без подписей) ----------

            var baseCanon = Canonicalize(decoded[0]);
            for (int i = 1; i < decoded.Count; i++)
            {
                if (!JToken.DeepEquals(baseCanon, Canonicalize(decoded[i])))
                    throw new InvalidOperationException("Incompatible Batch bodies. All inputs must have identical non-signing fields.");
            }

            // ---------- 3) base для результата ----------

            var combined = (JObject)decoded[0].DeepClone();
            combined.Remove("BatchSigners");
            combined.Remove("Signers");
            combined.Remove("TxnSignature");
            combined.Remove("SigningPubKey");

            // ---------- 4) собираем и мержим BatchSigners (inner-подписи) ----------

            var byAccount = new Dictionary<string, JObject>(StringComparer.Ordinal); // Account -> BatchSigner object

            foreach (var outer in decoded)
            {
                var arr = outer["BatchSigners"] as JArray;
                if (arr == null) continue;

                foreach (var w in arr.Children<JObject>())
                {
                    var bs = w["BatchSigner"] as JObject ?? w;
                    var accRaw = (string?)bs["Account"] ?? throw new InvalidOperationException("BatchSigner missing Account.");

                    var acc = ToClassic(accRaw);
                    bs["Account"] = acc; // нормализуем

                    if (!byAccount.TryGetValue(acc, out var existing))
                    {
                        byAccount[acc] = (JObject)bs.DeepClone();
                    }
                    else
                    {
                        // Уже есть BatchSigner по этому аккаунту → мержим (твоя существующая логика)
                        MergeBatchSigner(existing, bs);
                    }
                }
            }

            // Внутри каждого BatchSigner тоже может быть multisign (Signers[])
            // Делаем dedupe + сортировку по AccountID для внутренних Signers (по аналогии с CombineMultiSigners)
            foreach (var kvp in byAccount.ToList())
            {
                var bs = kvp.Value;
                var signersArr = bs["Signers"] as JArray;
                if (signersArr == null || signersArr.Count == 0)
                    continue;

                // dedupe по (Account, SigningPubKey, TxnSignature)
                var map = new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var item in signersArr.Children<JObject>())
                {
                    var signer = item["Signer"] as JObject ?? item;
                    var acc = (string?)signer["Account"] ?? "";
                    var pk = (string?)signer["SigningPubKey"] ?? "";
                    var sig = (string?)signer["TxnSignature"] ?? "";
                    var key = $"{ToClassic(acc)}|{pk}|{sig}";

                    if (!map.ContainsKey(key))
                        map[key] = (JObject)item.DeepClone();
                }

                var sorted = map.Values
                    .OrderBy(j =>
                    {
                        var acc = (string?)j["Signer"]?["Account"] ?? "";
                        return AID(acc);
                    }, Comparer<byte[]>.Create((a, b) =>
                    {
                        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                        {
                            int d = a[i].CompareTo(b[i]);
                            if (d != 0) return d;
                        }
                        return a.Length.CompareTo(b.Length);
                    }))
                    .ToArray();

                bs["Signers"] = new JArray(sorted);
            }

            // Собираем в массив-обёртку
            var mergedBatchSignersArr = new JArray(byAccount.Values.Select(v => new JObject { ["BatchSigner"] = v }));
            combined["BatchSigners"] = SortBatchSigners(mergedBatchSignersArr);

            // ---------- 5) собираем и мержим root Signers (top multisign) ----------

            var allRootSigners = new List<JObject>();

            foreach (var outer in decoded)
            {
                if (outer["Signers"] is not JArray arr) continue;
                foreach (var it in arr.Children<JObject>())
                {
                    allRootSigners.Add((JObject)it.DeepClone());
                }
            }

            JArray? mergedRootSigners = null;

            if (allRootSigners.Count > 0)
            {
                // dedupe по (Account, SigningPubKey, TxnSignature)
                var map = new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var j in allRootSigners)
                {
                    var signer = j["Signer"] as JObject ?? j;
                    var acc = (string?)signer["Account"] ?? "";
                    var pk = (string?)signer["SigningPubKey"] ?? "";
                    var sig = (string?)signer["TxnSignature"] ?? "";
                    var key = $"{ToClassic(acc)}|{pk}|{sig}";

                    if (!map.ContainsKey(key))
                        map[key] = (JObject)j.DeepClone();
                }

                // сортируем как в CombineMultiSigners
                var sorted = map.Values
                    .OrderBy(j =>
                    {
                        var acc = (string?)j["Signer"]?["Account"] ?? "";
                        return AID(acc);
                    }, Comparer<byte[]>.Create((a, b) =>
                    {
                        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                        {
                            int d = a[i].CompareTo(b[i]);
                            if (d != 0) return d;
                        }
                        return a.Length.CompareTo(b.Length);
                    }))
                    .ToArray();

                mergedRootSigners = new JArray(sorted);
                combined["Signers"] = mergedRootSigners;

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
                    var s = (string?)o["TxnSignature"];
                    var p = (string?)o["SigningPubKey"];

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

        /// <summary>
        /// Сливает входящий BatchSigner в существующий:
        /// - если у любого из них есть "Signers" (мультисиг), используем/объединяем "Signers";
        /// - single-sig (SigningPubKey/TxnSignature) под тем же Account не дублируем;
        /// - при конфликте single-sig vs Signers — оставляем Signers (они более общий случай).
        /// </summary>
        private static void MergeBatchSigner(JObject target, JObject incoming)
        {
            // оба мультисиг?
            var targetSigners = target["Signers"] as JArray;
            var incomingSigners = incoming["Signers"] as JArray;

            if (incomingSigners != null)
            {
                // если target был single-sig — преобразуем в "Signers" модель (просто забываем single; нельзя адекватно «конвертнуть» single в мульти)
                if (targetSigners == null)
                {
                    target.Remove("SigningPubKey");
                    target.Remove("TxnSignature");
                    targetSigners = new JArray();
                    target["Signers"] = targetSigners;
                }
                // merge всех Signer'ов: уникальность по (Signer.Account, SigningPubKey, TxnSignature)
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var s in targetSigners.Children<JObject>())
                {
                    var so = s["Signer"] as JObject;
                    if (so == null) continue;
                    var key = $"{(string?)so["Account"]}|{(string?)so["SigningPubKey"]}|{(string?)so["TxnSignature"]}";
                    seen.Add(key);
                }
                foreach (var s in incomingSigners.Children<JObject>())
                {
                    var so = s["Signer"] as JObject;
                    if (so == null) continue;
                    var key = $"{(string?)so["Account"]}|{(string?)so["SigningPubKey"]}|{(string?)so["TxnSignature"]}";
                    if (seen.Add(key))
                        targetSigners!.Add(new JObject { ["Signer"] = (JObject)so.DeepClone() });
                }
                return;
            }

            // incoming single-sig
            if (targetSigners != null)
            {
                // target уже мультисиг — single подпись игнорируем
                return;
            }

            // оба single-sig: оставляем первый; если тот же pubkey/sig — всё ок, если другой — игнорируем дубликат под тем же Account
            var tPub = (string?)target["SigningPubKey"];
            var tSig = (string?)target["TxnSignature"];
            var iPub = (string?)incoming["SigningPubKey"];
            var iSig = (string?)incoming["TxnSignature"];

            if (string.Equals(tPub, iPub, StringComparison.Ordinal)
                && string.Equals(tSig, iSig, StringComparison.Ordinal))
            {
                return; // идентичная подпись — уже есть
            }
            // разные single-подписи под одним Account — спецификацией не предполагается держать несколько; оставим первую.
        }

        /// <summary>Декодирует hex blob в JObject (терпим разные возвращаемые типы Decode).</summary>
        private static JObject DecodeToObject(string blobHex)
        {
            var dec = XrplBinaryCodec.Decode(blobHex);

            //if (dec is string s) return JObject.Parse(s);
            if (dec is JObject jo) return jo;
            return JObject.FromObject(dec!);
        }
    }
}