using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Xrpl.Keypairs.Utils;
using static Xrpl.AddressCodec.Utils;

namespace Xrpl.Keypairs.K256
{
    using ECDSASigner = Org.BouncyCastle.Crypto.Signers.ECDsaSigner;
    using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;
    using ECPrivateKeyParameters = Org.BouncyCastle.Crypto.Parameters.ECPrivateKeyParameters;
    using HMacDsaKCalculator = Org.BouncyCastle.Crypto.Signers.HMacDsaKCalculator;
    using Sha256Digest = Org.BouncyCastle.Crypto.Digests.Sha256Digest;

    public class K256KeyPair : IXrplKeyPair
    {
        private readonly BigInteger _privKey;
        private bool _isNodeKey;

        public K256KeyPair(BigInteger priv, bool nodeKey = false)
        {
            _privKey = priv;
            _isNodeKey = nodeKey;
        }

        public string Id()
        {
            return K256KeyGenerator.ComputePublicKey(this._privKey).GetEncoded(true).ToHex();
        }

        public string Pk()
        {
            // Always 33 bytes: a key with leading zero bytes is still written as 32, as xrpl.js does.
            return $"00{_privKey.ToByteArrayUnsigned().ToHex().PadLeft(64, '0')}";
        }

        /// <summary>Signs the SHA-512Half of <paramref name="message"/>, with a canonical (low) S.</summary>
        /// <param name="message">The message bytes.</param>
        /// <param name="privateKey">
        /// The private key, unsigned big-endian: the 33-byte form with the 00 prefix or the raw 32 bytes.
        /// It is read as unsigned, so a raw key whose first byte is 0x80 or above is not taken as negative.
        /// </param>
        public static byte[] Sign(byte[] message, byte[] privateKey)
        {
            ECDSASigner signer = new ECDSASigner(new HMacDsaKCalculator(new Sha256Digest()));
            ECPrivateKeyParameters parameters = new ECPrivateKeyParameters("ECDSA", new BigInteger(1, privateKey), Secp256K1.Parameters());
            signer.Init(true, parameters);
            BigInteger[] sigs = signer.GenerateSignature(Sha512.Half(message));
            var r = sigs[0];
            var s = sigs[1];

            var otherS = Secp256K1.Order().Subtract(s);
            if (s.CompareTo(otherS) == 1)
            {
                s = otherS;
            }

            return new EcdsaSignature(r, s).EncodeToDer();
        }

        public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
        {
            ECDsaSigner verifier = new ECDsaSigner();
            ECPublicKeyParameters parameters = new ECPublicKeyParameters(Secp256K1.Curve().DecodePoint(publicKey), Secp256K1.Parameters());
            verifier.Init(false, parameters);
            EcdsaSignature sig = EcdsaSignature.DecodeFromDer(signature);
            return sig != null && verifier.VerifySignature(Sha512.Half(message), sig.R, sig.S);
        }
    }
}