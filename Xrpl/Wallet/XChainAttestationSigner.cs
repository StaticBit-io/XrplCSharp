#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Keypairs;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

namespace Xrpl.Wallet
{
    /// <summary>
    /// Witness-side signing of XLS-38 bridge attestations. A witness that saw an
    /// <c>XChainCommit</c> or <c>XChainCreateAccountCommit</c> on one chain attests it on the
    /// other by submitting <c>XChainAddClaimAttestation</c> /
    /// <c>XChainAddAccountCreateAttestation</c> carrying its public key and a signature over
    /// the attested facts. The signed message is the canonical serialization of an STObject
    /// holding exactly those facts (rippled <c>AttestationClaim::message</c> /
    /// <c>AttestationCreateAccount::message</c>): no hash prefix, no transaction fields, so the
    /// same bytes verify regardless of which account submits the attestation transaction.
    /// </summary>
    public static class XChainAttestationSigner
    {
        /// <summary>
        /// Builds the bytes a witness signs for a claim attestation, from the fields the
        /// attestation transaction will carry.
        /// </summary>
        public static byte[] ClaimMessage(
            XChainBridgeModel bridge,
            string otherChainSource,
            Currency amount,
            string attestationRewardAccount,
            bool wasLockingChainSend,
            string xChainClaimId,
            string? destination)
        {
            JsonObject message = new JsonObject
            {
                ["XChainClaimID"] = Require(xChainClaimId, nameof(xChainClaimId)),
                ["Amount"] = ToNode(Require(amount, nameof(amount))),
                ["OtherChainSource"] = Require(otherChainSource, nameof(otherChainSource)),
                ["AttestationRewardAccount"] = Require(attestationRewardAccount, nameof(attestationRewardAccount)),
                ["WasLockingChainSend"] = JsonValue.Create((byte)(wasLockingChainSend ? 1 : 0)),
                ["XChainBridge"] = ToNode(Require(bridge, nameof(bridge))),
            };
            if (!string.IsNullOrEmpty(destination))
                message["Destination"] = destination;

            return AddressCodec.Utils.FromHex(XrplBinaryCodec.Encode(message));
        }

        /// <summary>
        /// Builds the bytes a witness signs for an account-create attestation.
        /// </summary>
        public static byte[] AccountCreateMessage(
            XChainBridgeModel bridge,
            string otherChainSource,
            Currency amount,
            Currency signatureReward,
            string destination,
            string attestationRewardAccount,
            bool wasLockingChainSend,
            string xChainAccountCreateCount)
        {
            JsonObject message = new JsonObject
            {
                ["XChainAccountCreateCount"] = Require(xChainAccountCreateCount, nameof(xChainAccountCreateCount)),
                ["Amount"] = ToNode(Require(amount, nameof(amount))),
                ["SignatureReward"] = ToNode(Require(signatureReward, nameof(signatureReward))),
                ["Destination"] = Require(destination, nameof(destination)),
                ["OtherChainSource"] = Require(otherChainSource, nameof(otherChainSource)),
                ["AttestationRewardAccount"] = Require(attestationRewardAccount, nameof(attestationRewardAccount)),
                ["WasLockingChainSend"] = JsonValue.Create((byte)(wasLockingChainSend ? 1 : 0)),
                ["XChainBridge"] = ToNode(Require(bridge, nameof(bridge))),
            };

            return AddressCodec.Utils.FromHex(XrplBinaryCodec.Encode(message));
        }

        /// <summary>
        /// Signs the attestation with the witness key: fills <c>PublicKey</c> and
        /// <c>Signature</c> from the transaction's own fields, and
        /// <c>AttestationSignerAccount</c> with the witness address when it is not set.
        /// The submitting <c>Account</c> may be any funded account.
        /// </summary>
        public static XChainAddClaimAttestation SignClaimAttestation(XChainAddClaimAttestation attestation, XrplWallet witness)
        {
            if (attestation is null) throw new ArgumentNullException(nameof(attestation));
            if (witness is null) throw new ArgumentNullException(nameof(witness));

            attestation.AttestationSignerAccount ??= witness.ClassicAddress;
            byte[] message = ClaimMessage(
                attestation.XChainBridge,
                attestation.OtherChainSource,
                attestation.Amount,
                attestation.AttestationRewardAccount,
                IsSet(attestation.WasLockingChainSend),
                attestation.XChainClaimID,
                attestation.Destination);

            attestation.PublicKey = witness.PublicKey;
            attestation.Signature = XrplKeypairs.Sign(message, witness.PrivateKey);
            return attestation;
        }

        /// <summary>
        /// Signs the account-create attestation with the witness key; see
        /// <see cref="SignClaimAttestation"/>.
        /// </summary>
        public static XChainAddAccountCreateAttestation SignAccountCreateAttestation(XChainAddAccountCreateAttestation attestation, XrplWallet witness)
        {
            if (attestation is null) throw new ArgumentNullException(nameof(attestation));
            if (witness is null) throw new ArgumentNullException(nameof(witness));

            attestation.AttestationSignerAccount ??= witness.ClassicAddress;
            byte[] message = AccountCreateMessage(
                attestation.XChainBridge,
                attestation.OtherChainSource,
                attestation.Amount,
                attestation.SignatureReward,
                attestation.Destination,
                attestation.AttestationRewardAccount,
                IsSet(attestation.WasLockingChainSend),
                attestation.XChainAccountCreateCount);

            attestation.PublicKey = witness.PublicKey;
            attestation.Signature = XrplKeypairs.Sign(message, witness.PrivateKey);
            return attestation;
        }

        /// <summary>
        /// Checks the attestation's signature against its own fields and public key,
        /// the way rippled's <c>attestationPreflight</c> does (temXCHAIN_BAD_PROOF otherwise).
        /// </summary>
        public static bool VerifyClaimAttestation(XChainAddClaimAttestation attestation)
        {
            if (attestation is null) throw new ArgumentNullException(nameof(attestation));
            if (string.IsNullOrEmpty(attestation.PublicKey) || string.IsNullOrEmpty(attestation.Signature))
                return false;

            byte[] message = ClaimMessage(
                attestation.XChainBridge,
                attestation.OtherChainSource,
                attestation.Amount,
                attestation.AttestationRewardAccount,
                IsSet(attestation.WasLockingChainSend),
                attestation.XChainClaimID,
                attestation.Destination);
            return XrplKeypairs.Verify(message, attestation.Signature, attestation.PublicKey);
        }

        /// <summary>
        /// Checks the account-create attestation's signature; see <see cref="VerifyClaimAttestation"/>.
        /// </summary>
        public static bool VerifyAccountCreateAttestation(XChainAddAccountCreateAttestation attestation)
        {
            if (attestation is null) throw new ArgumentNullException(nameof(attestation));
            if (string.IsNullOrEmpty(attestation.PublicKey) || string.IsNullOrEmpty(attestation.Signature))
                return false;

            byte[] message = AccountCreateMessage(
                attestation.XChainBridge,
                attestation.OtherChainSource,
                attestation.Amount,
                attestation.SignatureReward,
                attestation.Destination,
                attestation.AttestationRewardAccount,
                IsSet(attestation.WasLockingChainSend),
                attestation.XChainAccountCreateCount);
            return XrplKeypairs.Verify(message, attestation.Signature, attestation.PublicKey);
        }

        private static bool IsSet(byte? wasLockingChainSend) => wasLockingChainSend is > 0;

        private static JsonNode ToNode(object value) =>
            JsonSerializer.SerializeToNode(value, XrplJsonOptions.Default)
            ?? throw new ValidationException($"{value.GetType().Name} serialized to null.");

        private static T Require<T>(T? value, string name) where T : class =>
            value is null || (value is string s && s.Length == 0)
                ? throw new ValidationException($"Attestation field {name} is required.")
                : value;
    }
}
