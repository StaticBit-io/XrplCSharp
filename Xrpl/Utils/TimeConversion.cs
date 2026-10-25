using System;
using System.Globalization;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/timeConversion.ts

namespace Xrpl.Utils
{
    public static class DateTimeUtils
    {
        private const int RIPPLE_EPOCH_DIFF = 0x386d4380;

        public static long RippleTimeToUnixTime(long rpepoch)
        {
            return (rpepoch + RIPPLE_EPOCH_DIFF) * 1000;
        }

        public static long UnixTimeToRippleTime(long timestamp)
        {
            return (long)Math.Round((decimal)timestamp / 1000) - RIPPLE_EPOCH_DIFF;
        }

        public static string RippleTimeToISOTime(long rippleTime)
        {
            var timestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddMilliseconds(RippleTimeToUnixTime(rippleTime)).ToUniversalTime();
            return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        public static long ISOTimeToRippleTime(string iso8601)
        {
            // Parsed straight to UTC instead of into the machine's zone and back, so the result
            // never passes through local time.
            DateTime date = DateTime.ParseExact(iso8601, "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            return ISOTimeToRippleTime(date);
        }

        /// <summary>
        /// Ripple-epoch seconds of <paramref name="date"/>.
        /// </summary>
        /// <param name="date">A <see cref="DateTimeKind.Local"/> value is converted to UTC first;
        /// an <see cref="DateTimeKind.Unspecified"/> one is taken as UTC.</param>
        public static long ISOTimeToRippleTime(DateTime date)
        {
            DateTime utc = date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date;
            long milliseconds = (long)utc.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return UnixTimeToRippleTime(milliseconds);
        }
    }
}