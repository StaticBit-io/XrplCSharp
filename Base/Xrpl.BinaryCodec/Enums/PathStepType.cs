using System;

// https://xrpl.org/serialization.html#pathset-fields
namespace Xrpl.BinaryCodec.Enums
{
    /// <summary>
    /// Bitmask describing which fields a path step carries.<br/>
    /// Mirrors STPathElement in rippled: the byte is written in front of every hop of a serialized PathSet
    /// and is derived from the fields actually present, never taken from JSON input.<br/>
    /// rippled keeps two more values in the same enum that are not step types: TypeNone (0x00) terminates
    /// a PathSet and TypeBoundary (0xFF) separates paths — both live as byte constants on
    /// <see cref="Xrpl.BinaryCodec.Types.PathSet"/>.
    /// </summary>
    [Flags]
    public enum PathStepType : uint
    {
        /// <summary> No field present. </summary>
        None = 0x00,
        /// <summary> Rippling through an account (as opposed to taking an offer). </summary>
        Account = 0x01,
        /// <summary> A currency follows, changing the asset through an order book. </summary>
        Currency = 0x10,
        /// <summary> An issuer follows. </summary>
        Issuer = 0x20,
        /// <summary> An MPTokenIssuanceID follows (rippled 3.2.0+, MPTokensV2 amendment). </summary>
        MPTokenIssuanceID = 0x40,
        /// <summary> Every bit a hop type byte is allowed to carry. </summary>
        All = Account | Currency | Issuer | MPTokenIssuanceID,
    }
}
