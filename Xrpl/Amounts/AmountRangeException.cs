using System;

namespace Xrpl.Amounts
{
    /// <summary>
    /// An XRP or MPT value that is computed but lies beyond what the asset holds: rippled's
    /// <c>std::runtime_error</c> from <c>STAmount::canonicalize</c>, as opposed to the
    /// <c>std::overflow_error</c> of an arithmetic that cannot be carried out. rippled catches the
    /// two apart (<c>tryMulRound</c> in <c>Quality.cpp</c>), so the port keeps them apart too.
    /// </summary>
    internal sealed class AmountRangeException : OverflowException
    {
        public AmountRangeException(string message)
            : base(message)
        {
        }
    }
}
