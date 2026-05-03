using Xrpl.Utils;

namespace Xrpl.Models.Common
{
    /// <summary>
    /// Decoded components of an MPTokenIssuanceID (XLS-33).
    /// </summary>
    public class MPTokenIssuanceIdData
    {
        public MPTokenIssuanceIdData(uint sequence, string issuer)
        {
            Sequence = sequence;
            Issuer = issuer;
            MPTokenIssuanceID = ParseMPTID.GenerateMPTokenIssuanceID(sequence, issuer);
        }

        /// <summary>
        /// 48-hex uppercase MPTokenIssuanceID.
        /// </summary>
        public string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// Transaction sequence (or ticket) that created the issuance.
        /// </summary>
        public uint Sequence { get; set; }

        /// <summary>
        /// Issuer classic r-address.
        /// </summary>
        public string Issuer { get; set; }

        public static MPTokenIssuanceIdData ParseMPTokenIssuanceId(string mptIssuanceId)
        {
            return mptIssuanceId.ParseMPTokenIssuanceID();
        }
    }
}
