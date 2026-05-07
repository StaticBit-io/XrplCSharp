using System;

namespace Xrpl.Models.Enums;

[Flags]
public enum XrplGlobalFlags : uint
{
    /// <summary>
    /// DEPRECATED No effect.
    /// (If the RequireFullyCanonicalSig amendment is not enabled, this flag enforces a fully-canonical signature.)
    /// </summary>
    tfFullyCanonicalSig = 0x80000000,
    /// <summary>
    /// This flag is only used if a transaction is an inner transaction in a Batch transaction.<br/>
    /// This signifies that the transaction isn't signed.<br/>
    /// Any normal transaction that includes this flag is rejected.
    /// </summary>
    tfInnerBatchTxn = 0x40000000,
}