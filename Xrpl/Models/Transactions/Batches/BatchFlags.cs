using System;

namespace Xrpl.Models.Transactions.Batches;

[Flags]
public enum BatchFlags : uint
{
    /// <summary>
    /// In ALLORNOTHING mode, all inner transactions must succeed for any one of them to succeed.
    /// </summary>
    tfAllOrNothing = 0x00010000,
    /// <summary>
    /// ONLYONE mode means that the first transaction to succeed is the only one to succeed.
    /// All other transactions either failed or were never tried.
    /// </summary>
    tfOnlyOne = 0x00020000,
    /// <summary>
    /// UNTILFAILURE applies all transactions until the first failure. All transactions after the first failure are not applied.
    /// </summary>
    tfUntilFailure = 0x00040000,
    /// <summary>
    /// All transactions are applied, even if one or more of the inner transactions fail.
    /// </summary>
    tfIndependent = 0x00080000,
}

[Flags]
public enum BatchGlobalFlags : uint
{
    tfInnerBatchTxn = 0x40000000
}