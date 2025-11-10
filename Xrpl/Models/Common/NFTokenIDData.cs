using System;

using Xrpl.Utils;
namespace Xrpl.Models.Common
{
	public class NFTokenIdData
	{
        public NFTokenIdData(UInt32 flags, UInt32 transferFee, string issuer, UInt32 taxon, UInt32 sequence)
        {
            Flags = flags;
            TransferFee = transferFee;
            Issuer = issuer;
            Taxon = taxon;
            Sequence = sequence;
            NFTokenID = ParseNFTID.GenerateTokenId(flags, transferFee, issuer, taxon, sequence);
        }

        public string NFTokenID { get; set; }

        public uint Flags { get; set; }

        public uint TransferFee { get; set; }

        public string Issuer { get; set; }

        public uint Taxon { get; set; }

        public uint Sequence { get; set; }

        public static NFTokenIdData ParseNfTokenId(string nftokenID)
        {
            return nftokenID.ParseNFTokenID();
        }
    }
}

