/// <summary>
/// High-precision decimal math operations for financial calculations.
/// Provides Sqrt, Power, Log, Exp and related functions using Taylor series
/// and Newton's method, avoiding double-precision rounding errors.
/// </summary>

using System;

namespace Xrpl.Utils
{
    internal static class DecimalMath
    {
        public const decimal Epsilon = 0.0000000000000000001M;
        public const decimal E = 2.7182818284590452353602874713526624977572470936999595749M;
        private const decimal Einv = 0.3678794411714423215955237701614608674458111310317678M;
        private const decimal Log10Inv = 0.434294481903251827651128918916605082294397005803666566114M;
        public const decimal Zero = 0.0M;
        public const decimal One = 1.0M;
        private const decimal Half = 0.5M;
        private const int MaxIteration = 100;

        public static decimal Exp(decimal x)
        {
            var count = 0;
            if (x > One)
            {
                count = decimal.ToInt32(decimal.Truncate(x));
                x -= decimal.Truncate(x);
            }
            if (x < Zero)
            {
                count = decimal.ToInt32(decimal.Truncate(x) - 1);
                x = One + (x - decimal.Truncate(x));
            }
            var iteration = 1;
            var result = One;
            var factorial = One;
            decimal cachedResult;
            do
            {
                cachedResult = result;
                factorial *= x / iteration++;
                result += factorial;
            } while (cachedResult != result);
            if (count == 0)
                return result;
            return result * PowerN(E, count);
        }

        public static decimal Power(decimal value, decimal pow)
        {
            if (pow == Zero) return One;
            if (pow == One) return value;
            if (value == One) return One;
            if (value == Zero)
            {
                if (pow > Zero) return Zero;
                throw new InvalidOperationException("Invalid Operation: zero base and negative power");
            }
            if (pow == -One) return One / value;
            var isPowerInteger = IsInteger(pow);
            if (value < Zero && !isPowerInteger)
                throw new InvalidOperationException("Invalid Operation: negative base and non-integer power");
            if (isPowerInteger && value > Zero)
            {
                int powerInt = (int)pow;
                return PowerN(value, powerInt);
            }
            if (isPowerInteger && value < Zero)
            {
                int powerInt = (int)pow;
                if (powerInt % 2 == 0)
                    return Exp(pow * Log(-value));
                return -Exp(pow * Log(-value));
            }
            return Exp(pow * Log(value));
        }

        private static bool IsInteger(decimal value)
        {
            var longValue = (long)value;
            return Abs(value - longValue) <= Epsilon;
        }

        public static decimal PowerN(decimal value, int power)
        {
            while (true)
            {
                if (power == 0) return One;
                if (power < 0)
                {
                    value = One / value;
                    power = -power;
                    continue;
                }
                var q = power;
                var prod = One;
                var current = value;
                while (q > 0)
                {
                    if (q % 2 == 1)
                    {
                        prod = current * prod;
                        q--;
                    }
                    current *= current;
                    q >>= 1;
                }
                return prod;
            }
        }

        public static decimal Log10(decimal x) => Log(x) * Log10Inv;

        public static decimal Log(decimal x)
        {
            if (x <= Zero)
                throw new ArgumentException("x must be greater than zero");
            var count = 0;
            while (x >= One)
            {
                x *= Einv;
                count++;
            }
            while (x <= Einv)
            {
                x *= E;
                count--;
            }
            x--;
            if (x == Zero) return count;
            var result = Zero;
            var iteration = 0;
            var y = One;
            var cacheResult = result - One;
            while (cacheResult != result && iteration < MaxIteration)
            {
                iteration++;
                cacheResult = result;
                y *= -x;
                result += y / iteration;
            }
            return count - result;
        }

        public static decimal Sqrt(decimal x, decimal epsilon = Zero)
        {
            if (x < Zero) throw new OverflowException("Cannot calculate square root from a negative number");
            if (x == Zero) return Zero;
            decimal current = (decimal)Math.Sqrt((double)x), previous;
            var iteration = 0;
            do
            {
                previous = current;
                if (previous == Zero) return Zero;
                current = (previous + x / previous) * Half;
            } while (Abs(previous - current) > epsilon && ++iteration < MaxIteration);
            return current;
        }

        public static decimal Abs(decimal x) => x <= Zero ? -x : x;
    }
}
