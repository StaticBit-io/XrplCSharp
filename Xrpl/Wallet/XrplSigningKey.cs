using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;

using Xrpl.AddressCodec;
using Xrpl.Client.Json;
using Xrpl.Keypairs;
using Xrpl.Models.Transactions;

namespace Xrpl.Wallet
{
    /// <summary>
    /// A signing key that holds its private key in a byte array and clears it on
    /// <see cref="Dispose"/>, for an application that keeps keys encrypted at rest and decrypts one
    /// only for the signatures it needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Signs exactly as <see cref="XrplWallet"/> does - the same routing for Batch inner signers,
    /// Sponsor and Counterparty co-signatures and multi-signature entries, and byte-identical
    /// blobs - but the key never becomes a <see cref="string"/>, which .NET cannot clear and which
    /// may outlive its use in swap, a hibernation file or a memory dump.
    /// </para>
    /// <para>
    /// What stays outside the SDK's reach: secp256k1 signing builds the scalar as a BouncyCastle
    /// <c>BigInteger</c>, whose backing array cannot be cleared, and hash implementations keep
    /// state of their own. The public key and address are not secret and are kept as strings.
    /// </para>
    /// <para>
    /// Not safe for disposal concurrent with signing: dispose only once every signature has returned.
    /// </para>
    /// </remarks>
    public sealed class XrplSigningKey : IDisposable
    {
        private readonly byte[] _privateKey;
        private readonly XrplWallet _engine;
        private bool _disposed;

        /// <summary>
        /// Creates a signing key from a private key in the 33-byte XRPL form: <c>0xED</c> followed by
        /// the Ed25519 seed, or <c>0x00</c> followed by the secp256k1 scalar.
        /// </summary>
        /// <param name="privateKey">
        /// The key bytes. They are copied, so the caller can clear its own buffer as soon as this
        /// returns.
        /// </param>
        /// <param name="masterAddress">
        /// The account this key signs for when it is the account's regular key; the key's own
        /// address when null.
        /// </param>
        /// <exception cref="ArgumentException">The key is not 33 bytes or has an unknown prefix.</exception>
        public XrplSigningKey(ReadOnlySpan<byte> privateKey, string? masterAddress = null)
            : this(privateKey.ToArray(), XrplKeypairs.DerivePublicKey(privateKey), masterAddress)
        {
        }

        private XrplSigningKey(byte[] privateKey, byte[] publicKey, string? masterAddress)
        {
            _privateKey = privateKey;
            PublicKey = publicKey.ToHex();
            ClassicAddress = masterAddress ?? XrplKeypairs.DeriveAddressFromBytes(publicKey);
            _engine = new XrplWallet(PublicKey, ClassicAddress, SignMessage);
        }

        /// <summary>
        /// Derives the signing key of an account from 16 bytes of seed entropy without encoding a
        /// seed string: the key <see cref="XrplWallet.FromEntropy"/> derives from the same entropy.
        /// </summary>
        /// <param name="entropy">The 16 bytes of seed entropy; not retained.</param>
        /// <param name="algorithm"><see cref="XrplWallet.Ed25519"/> or <see cref="XrplWallet.Secp256k1"/>.</param>
        /// <param name="masterAddress">The account this key signs for when it is a regular key.</param>
        /// <exception cref="ArgumentException">The entropy is not 16 bytes, or the algorithm is unknown.</exception>
        public static XrplSigningKey FromEntropy(ReadOnlySpan<byte> entropy, string algorithm = XrplWallet.Ed25519, string? masterAddress = null)
        {
            (byte[] privateKey, byte[] publicKey) = XrplKeypairs.DeriveKeyBytes(entropy, algorithm);
            return new XrplSigningKey(privateKey, publicKey, masterAddress);
        }

        /// <summary>The public key, hex.</summary>
        public string PublicKey { get; }

        /// <summary>The classic address this key signs for: the master address when one was given.</summary>
        public string ClassicAddress { get; }

        /// <summary>Signs a transaction offline, as <see cref="XrplWallet.Sign(Dictionary{string, object}, bool, string?)"/> does.</summary>
        /// <param name="transaction">A transaction to be signed offline.</param>
        /// <param name="multisign">True to produce a multi-signature entry rather than a single signature.</param>
        /// <param name="signingFor">The signing account (classic or X-address); this key's own by default.</param>
        /// <exception cref="ObjectDisposedException">The key has been disposed.</exception>
        public SignatureResult Sign(Dictionary<string, object> transaction, bool multisign = false, string? signingFor = null)
        {
            ThrowIfDisposed();
            return _engine.Sign(transaction, multisign, signingFor);
        }

        /// <summary>
        /// Signs a transaction offline in a stated role, as
        /// <see cref="XrplWallet.Sign(Dictionary{string, object}, bool, string?, SignatureRole)"/> does.
        /// </summary>
        /// <param name="transaction">A transaction to be signed offline.</param>
        /// <param name="multisign">True to produce a multi-signature entry rather than a single signature.</param>
        /// <param name="signingFor">The signing account (classic or X-address); this key's own by default.</param>
        /// <param name="role">Which signature of the transaction this key is producing.</param>
        /// <exception cref="ObjectDisposedException">The key has been disposed.</exception>
        public SignatureResult Sign(Dictionary<string, object> transaction, bool multisign, string? signingFor, SignatureRole role)
        {
            ThrowIfDisposed();
            return _engine.Sign(transaction, multisign, signingFor, role);
        }

        /// <summary>Signs a typed transaction offline, as <see cref="XrplWallet.Sign(ITransactionRequest, bool, string?)"/> does.</summary>
        /// <exception cref="ObjectDisposedException">The key has been disposed.</exception>
        public SignatureResult Sign(ITransactionRequest transaction, bool multisign = false, string? signingFor = null) =>
            Sign(ToDictionary(transaction), multisign, signingFor);

        /// <summary>
        /// Signs a typed transaction offline in a stated role, as
        /// <see cref="XrplWallet.Sign(ITransactionRequest, bool, string?, SignatureRole)"/> does.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The key has been disposed.</exception>
        public SignatureResult Sign(ITransactionRequest transaction, bool multisign, string? signingFor, SignatureRole role) =>
            Sign(ToDictionary(transaction), multisign, signingFor, role);

        /// <summary>Clears the private key. Signing afterwards throws <see cref="ObjectDisposedException"/>.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            CryptographicOperations.ZeroMemory(_privateKey);
            _disposed = true;
        }

        /// <summary>The key buffer itself, so tests can see that <see cref="Dispose"/> cleared it.</summary>
        internal ReadOnlySpan<byte> KeyBuffer => _privateKey;

        private string SignMessage(byte[] message)
        {
            // Checked here as well as at each entry: the engine signs from inside its routing, and a
            // cleared buffer must never reach the signer as an all-zero key.
            ThrowIfDisposed();
            return XrplKeypairs.Sign((ReadOnlySpan<byte>)message, _privateKey).ToHex();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(XrplSigningKey));
        }

        private static Dictionary<string, object> ToDictionary(ITransactionRequest transaction) =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(transaction.ToJson(), XrplJsonOptions.Default);
    }
}
