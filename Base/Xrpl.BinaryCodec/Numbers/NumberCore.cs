using System;
using System.Collections.Generic;
using System.Numerics;

namespace Xrpl.BinaryCodec.Numbers
{
    /// <summary>
    /// A port of rippled's <c>Number</c> (<c>include/xrpl/basics/Number.h</c>,
    /// <c>src/libxrpl/basics/Number.cpp</c>), statement for statement, with the mantissa range and
    /// the rounding mode passed in a <see cref="NumberContext"/> instead of thread-local state.
    /// </summary>
    /// <remarks>
    /// The value is held as rippled holds it in the context's range: a sign, an unsigned mantissa
    /// normalized to [<see cref="NumberContext.MinMantissa"/>, <see cref="NumberContext.MaxMantissa"/>],
    /// an exponent in [-32768, 32768], and zero as (false, 0, int.MinValue). The 128-bit
    /// intermediates of the C++ code are <see cref="BigInteger"/>; every value they take fits in
    /// 128 bits, so the results are the same.
    /// </remarks>
    internal readonly struct NumberCore : IEquatable<NumberCore>
    {
        internal const int MinExponent = -32768;
        internal const int MaxExponent = 32768;
        internal const ulong MaxRep = long.MaxValue;
        internal const ulong MaxRepUp = 9_223_372_036_854_775_810UL;
        private const int ZeroExponent = int.MinValue;

        private static readonly BigInteger Ten = 10;

        internal NumberCore(bool negative, ulong mantissa, int exponent)
        {
            Negative = negative;
            Mantissa = mantissa;
            Exponent = exponent;
        }

        internal bool Negative { get; }

        internal ulong Mantissa { get; }

        internal int Exponent { get; }

        internal static NumberCore Zero => new NumberCore(false, 0, ZeroExponent);

        internal bool IsZero => Mantissa == 0;

        /// <summary><c>Number::mantissa()</c>: the signed mantissa of the wire form.</summary>
        internal long ExternalMantissa
        {
            get
            {
                ulong m = Mantissa > MaxRep ? Mantissa / 10 : Mantissa;
                return Negative ? -(long)m : (long)m;
            }
        }

        /// <summary><c>Number::exponent()</c>: the exponent of the wire form.</summary>
        internal int ExternalExponent => Mantissa > MaxRep ? Exponent + 1 : Exponent;

        // ---- construction -------------------------------------------------------------------

        /// <summary><c>Number(rep mantissa, int exponent)</c>: normalized in the context.</summary>
        internal static NumberCore FromWire(long mantissa, int exponent, NumberContext context)
        {
            bool negative = mantissa < 0;
            BigInteger m = BigInteger.Abs(new BigInteger(mantissa));
            return Normalized(negative, m, exponent, context);
        }

        /// <summary><c>Number(bool, internalrep, int, Normalized)</c>.</summary>
        internal static NumberCore FromInternal(bool negative, ulong mantissa, int exponent, NumberContext context) =>
            Normalized(negative, mantissa, exponent, context);

        /// <summary><c>Number::one()</c>.</summary>
        internal static NumberCore One(NumberContext context) =>
            new NumberCore(false, context.MinMantissa, -context.Log);

        private static NumberCore Normalized(bool negative, BigInteger mantissa, int exponent, NumberContext context)
        {
            DoNormalize(ref negative, ref mantissa, ref exponent, context, dropped: false);
            return new NumberCore(negative, (ulong)mantissa, exponent);
        }

        // ---- comparison ---------------------------------------------------------------------

        public bool Equals(NumberCore other) =>
            Negative == other.Negative && Mantissa == other.Mantissa && Exponent == other.Exponent;

        public override bool Equals(object obj) => obj is NumberCore other && Equals(other);

        public override int GetHashCode() => unchecked((Mantissa.GetHashCode() * 397) ^ Exponent ^ (Negative ? 1 : 0));

        /// <summary>rippled's <c>operator&lt;</c>.</summary>
        internal static bool Less(NumberCore l, NumberCore r)
        {
            bool lneg = l.Negative;
            bool rneg = r.Negative;
            if (lneg != rneg)
                return lneg;
            if (l.Mantissa == 0)
                return r.Mantissa > 0;
            if (r.Mantissa == 0)
                return false;
            if (l.Exponent > r.Exponent)
                return lneg;
            if (l.Exponent < r.Exponent)
                return !lneg;
            return lneg ? l.Mantissa > r.Mantissa : l.Mantissa < r.Mantissa;
        }

        internal NumberCore Negate() => Mantissa == 0 ? Zero : new NumberCore(!Negative, Mantissa, Exponent);

        internal NumberCore Abs() => Negative ? Negate() : this;

        // ---- normalization ------------------------------------------------------------------

        /// <summary>rippled's <c>doNormalize</c>.</summary>
        private static void DoNormalize(ref bool negative, ref BigInteger mantissa, ref int exponent, NumberContext context, bool dropped)
        {
            BigInteger minMantissa = context.MinMantissa;
            BigInteger maxMantissa = context.MaxMantissa;
            BigInteger repLimit = context.Cusp >= CuspRoundingFix.Enabled330 ? MaxRepUp : MaxRep;

            if (mantissa.IsZero)
            {
                negative = false;
                exponent = ZeroExponent;
                return;
            }

            BigInteger m = mantissa;
            while (m < minMantissa && exponent > MinExponent)
            {
                m *= Ten;
                --exponent;
            }

            Guard g = new Guard(context);
            if (negative)
                g.SetNegative();
            if (dropped)
                g.SetDropped();

            while (m > maxMantissa)
            {
                if (exponent >= MaxExponent)
                    throw new OverflowException("Number::normalize 1");
                g.DoDropDigit(ref m, ref exponent);
            }

            if (exponent < MinExponent || m < minMantissa)
            {
                negative = false;
                mantissa = BigInteger.Zero;
                exponent = ZeroExponent;
                return;
            }

            if (m > repLimit)
            {
                if (exponent >= MaxExponent)
                    throw new OverflowException("Number::normalize 1.5");
                g.DoDropDigit(ref m, ref exponent);
            }

            mantissa = m;
            g.DoRoundUp(ref negative, ref mantissa, ref exponent, "Number::normalize 2");
        }

        private static NumberCore Normalize(bool negative, BigInteger mantissa, int exponent, NumberContext context, bool dropped)
        {
            DoNormalize(ref negative, ref mantissa, ref exponent, context, dropped);
            return new NumberCore(negative, (ulong)mantissa, exponent);
        }

        /// <summary><c>Number::shiftExponent</c>.</summary>
        private NumberCore ShiftExponent(int exponentDelta)
        {
            int newExponent = Exponent + exponentDelta;
            if (newExponent >= MaxExponent)
                throw new OverflowException("Number::shiftExponent");
            if (newExponent < MinExponent)
                return Zero;
            return new NumberCore(Negative, Mantissa, newExponent);
        }

        // ---- arithmetic ---------------------------------------------------------------------

        /// <summary><c>Number::operator+=</c>.</summary>
        internal static NumberCore Add(NumberCore x, NumberCore y, NumberContext context)
        {
            if (y.IsZero)
                return x;
            if (x.IsZero)
                return y;
            if (x.Equals(y.Negate()))
                return Zero;

            bool xn = x.Negative;
            BigInteger xm = x.Mantissa;
            int xe = x.Exponent;

            bool yn = y.Negative;
            BigInteger ym = y.Mantissa;
            int ye = y.Exponent;
            Guard g = new Guard(context);

            BigInteger minMantissa = context.MinMantissa;
            BigInteger maxMantissa = context.MaxMantissa;
            CuspRoundingFix cusp = context.Cusp;
            BigInteger repLimit = cusp >= CuspRoundingFix.Enabled330 ? MaxRepUp : MaxRep;
            BigInteger upperLimit = minMantissa * 1000;

            void Adjust(ref BigInteger expandM, ref int expandE, ref BigInteger shrinkM, ref int shrinkE)
            {
                if (cusp == CuspRoundingFix.Enabled330)
                {
                    while (shrinkE < expandE && (shrinkM % Ten).IsZero)
                        g.DoDropDigit(ref shrinkM, ref shrinkE);

                    while (shrinkE < expandE && expandE > MinExponent && expandM < upperLimit)
                    {
                        expandM *= Ten;
                        --expandE;
                    }
                }

                if (shrinkE < expandE)
                    g.DoDropDigitWithTarget(ref shrinkM, ref shrinkE, expandE);
            }

            if (xe < ye)
            {
                if (xn)
                    g.SetNegative();
                Adjust(ref ym, ref ye, ref xm, ref xe);
            }
            else if (xe > ye)
            {
                if (yn)
                    g.SetNegative();
                Adjust(ref xm, ref xe, ref ym, ref ye);
            }
            else if (cusp == CuspRoundingFix.Enabled330)
            {
                if ((xm < ym && xn) || (ym < xm && yn))
                    g.SetNegative();
            }

            if (xn == yn)
            {
                xm += ym;
                if (cusp < CuspRoundingFix.Enabled330)
                {
                    if (xm > maxMantissa || xm > repLimit)
                        g.DoDropDigit(ref xm, ref xe);
                    g.DoRoundUp(ref xn, ref xm, ref xe, "Number::addition overflow");
                }
            }
            else
            {
                if (xm > ym)
                {
                    xm -= ym;
                }
                else
                {
                    xm = ym - xm;
                    xe = ye;
                    xn = yn;
                }

                if (cusp >= CuspRoundingFix.Enabled330)
                {
                    while (xm < upperLimit && !g.Empty)
                    {
                        xm *= Ten;
                        xm -= g.Pop();
                        --xe;
                    }
                }
                else
                {
                    while (xm < minMantissa && xm * Ten <= repLimit)
                    {
                        xm *= Ten;
                        xm -= g.Pop();
                        --xe;
                    }
                }

                g.DoRoundDown(ref xn, ref xm, ref xe);
            }

            return Normalize(xn, xm, xe, context, cusp == CuspRoundingFix.Enabled330 && !g.Empty);
        }

        internal static NumberCore Subtract(NumberCore x, NumberCore y, NumberContext context) => Add(x, y.Negate(), context);

        /// <summary><c>Number::operator*=</c>.</summary>
        internal static NumberCore Multiply(NumberCore x, NumberCore y, NumberContext context)
        {
            if (x.IsZero)
                return x;
            if (y.IsZero)
                return y;

            BigInteger zm = new BigInteger(x.Mantissa) * y.Mantissa;
            int ze = x.Exponent + y.Exponent;
            bool zn = x.Negative != y.Negative;
            Guard g = new Guard(context);
            if (zn)
                g.SetNegative();

            BigInteger maxMantissa = context.MaxMantissa;
            BigInteger repLimit = context.Cusp >= CuspRoundingFix.Enabled330 ? MaxRepUp : MaxRep;

            while (zm > maxMantissa || zm > repLimit)
                g.DoDropDigit(ref zm, ref ze);

            g.DoRoundUp(ref zn, ref zm, ref ze, "Number::multiplication overflow : exponent is " + ze);

            // normalize(g): the guard's range is the context's.
            return Normalize(zn, zm, ze, context, dropped: false);
        }

        /// <summary><c>Number::operator/=</c>.</summary>
        internal static NumberCore Divide(NumberCore x, NumberCore y, NumberContext context)
        {
            if (y.IsZero)
                throw new DivideByZeroException("Number: divide by 0");
            if (x.IsZero)
                return x;

            BigInteger dm = y.Mantissa;
            const int factorExponent = 17;
            BigInteger f = BigInteger.Pow(Ten, factorExponent);
            BigInteger numerator = new BigInteger(x.Mantissa) * f;

            BigInteger zm = BigInteger.DivRem(numerator, dm, out BigInteger remainder);
            int ze = x.Exponent - y.Exponent - factorExponent;
            bool zp = x.Negative != y.Negative;
            bool dropped = false;

            if (context.Scale != NumberMantissaScale.Small && !remainder.IsZero)
            {
                const int correctionExponent = 5;
                BigInteger correctionFactor = BigInteger.Pow(Ten, correctionExponent);
                BigInteger partialNumerator = remainder * correctionFactor;
                BigInteger correction = BigInteger.DivRem(partialNumerator, dm, out BigInteger partialRemainder);

                if (!correction.IsZero)
                {
                    zm *= correctionFactor;
                    ze -= correctionExponent;
                    zm += correction;
                }

                if (context.Cusp != CuspRoundingFix.Disabled)
                    dropped = !partialRemainder.IsZero;
            }

            return Normalize(zp, zm, ze, context, dropped);
        }

        /// <summary><c>Number::operator rep()</c>: the value as an integer, rounded in the context's mode.</summary>
        internal long ToInt64(NumberContext context)
        {
            BigInteger drops = ExternalMantissa;
            int offset = ExternalExponent;
            Guard g = new Guard(context);
            if (drops.IsZero)
                return 0;

            if (Negative)
            {
                g.SetNegative();
                drops = -drops;
            }

            if (offset < 0)
                g.DoDropDigitWithTarget(ref drops, ref offset, 0);

            for (; offset > 0; --offset)
            {
                if (drops > MaxRep / 10)
                    throw new OverflowException("Number::operator rep() overflow");
                drops *= Ten;
            }

            return g.DoRound((long)drops, "Number::operator rep() rounding overflow");
        }

        /// <summary><c>Number::truncate</c>.</summary>
        internal NumberCore Truncate(NumberContext context)
        {
            if (Exponent >= 0 || Mantissa == 0)
                return this;

            ulong m = Mantissa;
            int e = Exponent;
            while (e < 0 && m != 0)
            {
                e += 1;
                m /= 10;
            }

            return Normalize(Negative, m, e, context, dropped: false);
        }

        /// <summary><c>power(Number const&amp; f, unsigned n)</c>.</summary>
        internal static NumberCore Power(NumberCore f, uint n, NumberContext context)
        {
            if (n == 0)
                return One(context);
            if (n == 1)
                return f;
            NumberCore r = Power(f, n / 2, context);
            r = Multiply(r, r, context);
            if (n % 2 != 0)
                r = Multiply(r, f, context);
            return r;
        }

        /// <summary><c>root(Number f, unsigned d)</c>: f^(1/d) by Newton-Raphson.</summary>
        internal static NumberCore Root(NumberCore f, uint d, NumberContext context)
        {
            NumberCore one = One(context);

            if (f.Equals(one) || d == 1)
                return f;
            if (d == 0)
            {
                if (f.Equals(one.Negate()))
                    return one;
                if (Less(f.Abs(), one))
                    return Zero;
                throw new OverflowException("Number::root infinity");
            }

            if (Less(f, Zero) && d % 2 == 0)
                throw new OverflowException("Number::root nan");
            if (f.IsZero)
                return f;

            int e = f.Exponent + context.Log + 1;
            int di = (int)d;
            int k = (e >= 0 ? e : e - (di - 1)) / di;
            int k2 = e - (k * di);
            int ex = k2 == 0 ? 0 : di - k2;
            e += ex;
            f = f.ShiftExponent(-e);

            bool neg = false;
            if (Less(f, Zero))
            {
                neg = true;
                f = f.Negate();
            }

            int bigD = ((((((6 * di) + 11) * di) + 6) * di) + 1);
            int a0 = 3 * di * ((((2 * di) - 3) * di) + 1);
            int a1 = 24 * di * ((2 * di) - 1);
            int a2 = -30 * (di - 1) * di;
            NumberCore r = Divide(
                Add(Multiply(Add(Multiply(FromWire(a2, 0, context), f, context), FromWire(a1, 0, context), context), f, context), FromWire(a0, 0, context), context),
                FromWire(bigD, 0, context),
                context);
            if (neg)
            {
                f = f.Negate();
                r = r.Negate();
            }

            NumberCore rm1 = Zero;
            NumberCore rm2;
            NumberCore dMinusOne = FromWire(d - 1, 0, context);
            NumberCore dNumber = FromWire(d, 0, context);
            HashSet<NumberCore> seen = new HashSet<NumberCore>();
            do
            {
                rm2 = rm1;
                rm1 = r;
                r = Divide(Add(Multiply(dMinusOne, r, context), Divide(f, Power(r, d - 1, context), context), context), dNumber, context);
            }
            while (!r.Equals(rm1) && !r.Equals(rm2) && !IsCycling(seen, rm1, r, "Number::root"));

            return r.ShiftExponent(e / di);
        }

        /// <summary><c>root2(Number f)</c>: the square root.</summary>
        internal static NumberCore Root2(NumberCore f, NumberContext context)
        {
            NumberCore one = One(context);

            if (f.Equals(one))
                return f;
            if (Less(f, Zero))
                throw new OverflowException("Number::root nan");
            if (f.IsZero)
                return f;

            int e = f.Exponent + context.Log + 1;
            if (e % 2 != 0)
                ++e;
            f = f.ShiftExponent(-e);

            NumberCore r = Divide(
                Add(Multiply(Add(Multiply(FromWire(-60, 0, context), f, context), FromWire(144, 0, context), context), f, context), FromWire(18, 0, context), context),
                FromWire(105, 0, context),
                context);

            NumberCore two = FromWire(2, 0, context);
            NumberCore rm1 = Zero;
            NumberCore rm2;
            HashSet<NumberCore> seen = new HashSet<NumberCore>();
            do
            {
                rm2 = rm1;
                rm1 = r;
                r = Divide(Add(r, Divide(f, r, context), context), two, context);
            }
            while (!r.Equals(rm1) && !r.Equals(rm2) && !IsCycling(seen, rm1, r, "Number::root2"));

            return r.ShiftExponent(e / 2);
        }

        /// <summary>
        /// rippled stops a Newton-Raphson loop when the next value repeats the last one or the one
        /// before it. For <c>root(f, d)</c> with d of 3 or more the iteration can instead settle into
        /// a longer cycle, on every scale and in every rounding mode (a few inputs in a thousand),
        /// and rippled never leaves it; no production code path calls that function. A value seen
        /// before the previous two means such a cycle: it is reported instead of looping forever,
        /// and every input rippled returns from gives the same result.
        /// </summary>
        /// <returns>Always <see langword="false"/>, so the loop condition keeps iterating.</returns>
        /// <exception cref="ArithmeticException">The iteration is in a cycle longer than two.</exception>
        private static bool IsCycling(HashSet<NumberCore> seen, NumberCore previous, NumberCore next, string operation)
        {
            seen.Add(previous);
            if (seen.Contains(next))
                throw new ArithmeticException(operation + " does not converge");

            return false;
        }

        /// <summary><c>power(Number const&amp; f, unsigned n, unsigned d)</c>: f^(n/d).</summary>
        internal static NumberCore Power(NumberCore f, uint n, uint d, NumberContext context)
        {
            NumberCore one = One(context);

            if (f.Equals(one))
                return f;
            uint g = Gcd(n, d);
            if (g == 0)
                throw new OverflowException("Number::power nan");
            if (d == 0)
            {
                if (f.Equals(one.Negate()))
                    return one;
                if (Less(f.Abs(), one))
                    return Zero;
                throw new OverflowException("Number::power infinity");
            }

            if (n == 0)
                return one;
            n /= g;
            d /= g;
            if (n % 2 == 1 && d % 2 == 0 && Less(f, Zero))
                throw new OverflowException("Number::power nan");
            return Root(Power(f, n, context), d, context);
        }

        private static uint Gcd(uint a, uint b)
        {
            while (b != 0)
            {
                uint t = a % b;
                a = b;
                b = t;
            }

            return a;
        }

        /// <summary>
        /// rippled's <c>Number::Guard</c>: up to 16 decimal digits dropped from a mantissa, whether
        /// any non-zero digit fell off beyond them, and the sign, from which a result is rounded.
        /// </summary>
        private sealed class Guard
        {
            private readonly NumberContext _context;
            private readonly BigInteger _minMantissa;
            private readonly BigInteger _maxMantissa;
            private ulong _digits;
            private bool _xbit;
            private bool _sbit;

            internal Guard(NumberContext context)
            {
                _context = context;
                _minMantissa = context.MinMantissa;
                _maxMantissa = context.MaxMantissa;
            }

            private enum Round
            {
                Exact = -2,
                Down = -1,
                Even = 0,
                Up = 1,
            }

            private CuspRoundingFix Cusp => _context.Cusp;

            internal bool IsNegative => _sbit;

            internal bool Unrecoverable => _digits == 0;

            internal bool Empty => Unrecoverable && !_xbit;

            internal void SetNegative() => _sbit = true;

            internal void SetDropped() => _xbit = true;

            private void Push(uint d)
            {
                _xbit = _xbit || (_digits & 0xFUL) != 0;
                _digits >>= 4;
                _digits |= (ulong)(d & 0xF) << 60;
            }

            internal uint Pop()
            {
                uint d = (uint)((_digits & 0xF000_0000_0000_0000UL) >> 60);
                _digits <<= 4;
                return d;
            }

            internal void DoDropDigit(ref BigInteger mantissa, ref int exponent)
            {
                mantissa = BigInteger.DivRem(mantissa, Ten, out BigInteger digit);
                Push((uint)digit);
                ++exponent;
            }

            internal void DoDropDigitWithTarget(ref BigInteger mantissa, ref int exponent, int targetExponent)
            {
                while (exponent < targetExponent)
                {
                    if (mantissa.IsZero && Unrecoverable)
                    {
                        exponent = targetExponent;
                        return;
                    }

                    DoDropDigit(ref mantissa, ref exponent);
                }
            }

            private void PushOverflow(BigInteger mantissa)
            {
                if (Cusp >= CuspRoundingFix.Enabled330 && mantissa >= MaxRep && mantissa < MaxRepUp)
                {
                    const ulong spread = MaxRepUp - MaxRep;
                    if (mantissa % Ten < 9)
                    {
                        const ulong midpoint = MaxRep + (spread / 2);
                        Round r = RoundDirection();
                        if (r == Round.Up || (r == Round.Even && mantissa == midpoint))
                            mantissa += 1;
                    }

                    BigInteger diff = mantissa - MaxRep;
                    uint digit = (uint)(diff * 10 / spread);
                    Push(digit);
                }
            }

            private Round RoundDirection()
            {
                NumberRounding mode = _context.Rounding;

                if (Cusp >= CuspRoundingFix.Enabled330 && Empty)
                    return Round.Exact;

                if (mode == NumberRounding.TowardsZero)
                    return Round.Down;

                if ((mode == NumberRounding.Downward && !_sbit) || (mode == NumberRounding.Upward && _sbit))
                    return Round.Down;

                if (mode == NumberRounding.Downward || mode == NumberRounding.Upward)
                    return Empty ? Round.Down : Round.Up;

                if (_digits > 0x5000_0000_0000_0000UL)
                    return Round.Up;
                if (_digits < 0x5000_0000_0000_0000UL)
                    return Round.Down;
                if (_xbit)
                    return Round.Up;
                return Round.Even;
            }

            private void BringIntoRange(ref bool negative, ref BigInteger mantissa, ref int exponent)
            {
                if (mantissa < _minMantissa && (Cusp < CuspRoundingFix.Enabled330 || !mantissa.IsZero))
                {
                    mantissa *= Ten;
                    --exponent;
                }

                if (exponent < MinExponent || (Cusp >= CuspRoundingFix.Enabled330 && mantissa.IsZero))
                {
                    negative = false;
                    mantissa = BigInteger.Zero;
                    exponent = ZeroExponent;
                }
            }

            internal void DoRoundUp(ref bool negative, ref BigInteger mantissa, ref int exponent, string location)
            {
                PushOverflow(mantissa);

                Round r = RoundDirection();
                if (r == Round.Up || (r == Round.Even && !mantissa.IsEven))
                {
                    bool SafeToIncrement(BigInteger m) => m < _maxMantissa && m < MaxRep;

                    if (Cusp != CuspRoundingFix.Disabled)
                    {
                        if (SafeToIncrement(mantissa))
                        {
                            mantissa += 1;
                        }
                        else if (Cusp >= CuspRoundingFix.Enabled330 && mantissa > MaxRep && mantissa < MaxRepUp)
                        {
                            mantissa = MaxRepUp;
                        }
                        else
                        {
                            DoDropDigit(ref mantissa, ref exponent);
                            DoRoundUp(ref negative, ref mantissa, ref exponent, location);
                            return;
                        }
                    }
                    else
                    {
                        mantissa += 1;
                        if (mantissa > _maxMantissa || mantissa > MaxRep)
                        {
                            mantissa /= Ten;
                            ++exponent;
                        }
                    }
                }
                else if (Cusp >= CuspRoundingFix.Enabled330 && mantissa > MaxRep && mantissa < MaxRepUp)
                {
                    mantissa = MaxRep;
                }

                BringIntoRange(ref negative, ref mantissa, ref exponent);
                if (exponent > MaxExponent)
                    throw new OverflowException(location);
            }

            internal void DoRoundDown(ref bool negative, ref BigInteger mantissa, ref int exponent)
            {
                Round r = RoundDirection();
                if (Cusp >= CuspRoundingFix.Enabled330)
                {
                    if (r != Round.Exact)
                        mantissa -= 1;
                }
                else if (r == Round.Up || (r == Round.Even && !mantissa.IsEven))
                {
                    mantissa -= 1;
                    if (mantissa < _minMantissa)
                    {
                        mantissa *= Ten;
                        --exponent;
                    }
                }

                BringIntoRange(ref negative, ref mantissa, ref exponent);
            }

            internal long DoRound(long drops, string location)
            {
                Round r = RoundDirection();
                if (r == Round.Up || (r == Round.Even && (drops & 1) == 1))
                {
                    if ((ulong)drops >= MaxRep)
                        throw new OverflowException(location);
                    ++drops;
                }

                return IsNegative ? -drops : drops;
            }
        }
    }
}
