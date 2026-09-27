using System;

namespace Xrpl.BinaryCodec.Numbers
{
    /// <summary>
    /// How a <see cref="XrplNumber"/> operation rounds a result it cannot hold exactly, as
    /// rippled's <c>Number::RoundingMode</c>.
    /// </summary>
    public enum NumberRounding
    {
        /// <summary>To the nearest value; a tie goes to the even mantissa. rippled's default.</summary>
        ToNearest,

        /// <summary>Toward zero: the magnitude never grows.</summary>
        TowardsZero,

        /// <summary>Toward negative infinity.</summary>
        Downward,

        /// <summary>Toward positive infinity.</summary>
        Upward,
    }

    /// <summary>
    /// The mantissa range rippled's <c>Number</c> works in, which the enabled amendments select
    /// (<c>setCurrentTransactionRules</c> in <c>Rules.cpp</c>).
    /// </summary>
    public enum NumberMantissaScale
    {
        /// <summary>16 digits, [10^15, 10^16): without SingleAssetVault and LendingProtocol.</summary>
        Small,

        /// <summary>19 digits, [10^18, 10^19): SingleAssetVault or LendingProtocol, before <c>fixCleanup3_2_0</c>.</summary>
        LargeLegacy,

        /// <summary>19 digits with the <c>fixCleanup3_2_0</c> rounding at the 2^63 - 1 cusp.</summary>
        Large320,

        /// <summary>19 digits with the <c>fixCleanup3_3_0</c> rounding: exact results are never rounded.</summary>
        Large330,
    }

    /// <summary>
    /// The rules an <see cref="XrplNumber"/> operation follows: the mantissa scale and the rounding
    /// mode. rippled keeps both in thread-local state switched by guards; here they are an explicit
    /// argument, so a result never depends on what another piece of code set before.
    /// </summary>
    public sealed class NumberContext
    {
        private NumberContext(NumberMantissaScale scale, NumberRounding rounding)
        {
            Scale = scale;
            Rounding = rounding;
            Log = scale == NumberMantissaScale.Small ? 15 : 18;
            MinMantissa = Pow10(Log);
            MaxMantissa = MinMantissa * 10 - 1;
            Cusp = scale switch
            {
                NumberMantissaScale.Large320 => CuspRoundingFix.Enabled320,
                NumberMantissaScale.Large330 => CuspRoundingFix.Enabled330,
                _ => CuspRoundingFix.Disabled,
            };
        }

        /// <summary>The newest rules - <see cref="NumberMantissaScale.Large330"/> - rounding to nearest.</summary>
        public static NumberContext Default { get; } = new NumberContext(NumberMantissaScale.Large330, NumberRounding.ToNearest);

        /// <summary>The mantissa scale.</summary>
        public NumberMantissaScale Scale { get; }

        /// <summary>The rounding mode.</summary>
        public NumberRounding Rounding { get; }

        internal int Log { get; }

        internal ulong MinMantissa { get; }

        internal ulong MaxMantissa { get; }

        internal CuspRoundingFix Cusp { get; }

        /// <summary>Creates a context for a scale and a rounding mode.</summary>
        public static NumberContext Create(NumberMantissaScale scale, NumberRounding rounding = NumberRounding.ToNearest)
        {
            if (!Enum.IsDefined(typeof(NumberMantissaScale), scale))
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (!Enum.IsDefined(typeof(NumberRounding), rounding))
                throw new ArgumentOutOfRangeException(nameof(rounding));

            return scale == NumberMantissaScale.Large330 && rounding == NumberRounding.ToNearest
                ? Default
                : new NumberContext(scale, rounding);
        }

        /// <summary>
        /// The context rippled uses for a ledger with these amendments, rounding to nearest.
        /// </summary>
        /// <param name="largeNumbers">Whether SingleAssetVault or LendingProtocol is enabled.</param>
        /// <param name="fixCleanup320">Whether <c>fixCleanup3_2_0</c> is enabled.</param>
        /// <param name="fixCleanup330">Whether <c>fixCleanup3_3_0</c> is enabled.</param>
        public static NumberContext ForAmendments(bool largeNumbers, bool fixCleanup320, bool fixCleanup330)
        {
            NumberMantissaScale scale = !largeNumbers ? NumberMantissaScale.Small
                : fixCleanup330 ? NumberMantissaScale.Large330
                : fixCleanup320 ? NumberMantissaScale.Large320
                : NumberMantissaScale.LargeLegacy;
            return Create(scale);
        }

        /// <summary>The same scale with another rounding mode.</summary>
        public NumberContext WithRounding(NumberRounding rounding) => rounding == Rounding ? this : Create(Scale, rounding);

        /// <inheritdoc />
        public override string ToString() => $"{Scale}, {Rounding}";

        private static ulong Pow10(int exponent)
        {
            ulong result = 1;
            for (int i = 0; i < exponent; i++)
                result *= 10;

            return result;
        }
    }

    /// <summary>rippled's <c>MantissaRange::CuspRoundingFix</c>.</summary>
    internal enum CuspRoundingFix
    {
        Disabled = 0,
        Enabled320 = 1,
        Enabled330 = 2,
    }
}
