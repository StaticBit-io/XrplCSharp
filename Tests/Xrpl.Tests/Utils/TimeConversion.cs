

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/utils/timeConversion.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Globalization;
using Xrpl.Utils;

namespace XrplTests.Xrpl.Utils
{
    [TestClass]
    public class TestUTimeConversionTests
    {
        [TestMethod]
        public void TestRippleTimeToISOTime()
        {
            var rippleTime = 0;
            var isoTime = "2000-01-01T00:00:00.000Z";
            Assert.AreEqual(isoTime, DateTimeUtils.RippleTimeToISOTime(rippleTime));
        }

        [TestMethod]
        public void TestISOTimeToRippleTime()
        {
            var rippleTime = 0;
            var isoTime = "2000-01-01T00:00:00.000Z";
            Assert.AreEqual(DateTimeUtils.ISOTimeToRippleTime(isoTime), rippleTime);
        }

        [TestMethod]
        public void TestISOTimeToRippleTimeFromDate()
        {
            var rippleTime = 0;
            var isoTime = "2000-01-01T00:00:00.000Z";
            var date = DateTime.ParseExact(isoTime, "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture).ToUniversalTime();
            Assert.AreEqual(rippleTime, DateTimeUtils.ISOTimeToRippleTime(date));
        }

        [TestMethod]
        public void TestUnixTimeToRippleTime()
        {
            var unixTime = 946684801000;
            var rippleTime = 1;
            Assert.AreEqual(DateTimeUtils.UnixTimeToRippleTime(unixTime), rippleTime);
        }

        [TestMethod]
        public void TestRippleTimeToUnixTime()
        {
            var unixTime = 946684801000;
            var rippleTime = 1;
            Assert.AreEqual(DateTimeUtils.RippleTimeToUnixTime(rippleTime), unixTime);
        }
    }

    /// <summary>
    /// The conversions under a local zone other than UTC. Not parallel: the zone is process-wide.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class TestUTimeConversionLocalZone
    {
        private static readonly DateTime RippleEpoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// 08:30Z on the day US daylight saving time ends is 01:30 PDT, an hour that happens twice
        /// in local time - the one place a trip through local time could lose an hour. The string
        /// is now parsed straight to UTC, so the local zone plays no part.
        /// </summary>
        [TestMethod]
        public void ISOTimeToRippleTime_String_AmbiguousLocalHour_IsExact()
        {
            using LocalTimeZoneScope zone = LocalTimeZoneScope.Use(LocalTimeZoneScope.UsPacific);

            long expected = (long)(new DateTime(2024, 11, 3, 8, 30, 0, DateTimeKind.Utc) - RippleEpoch).TotalSeconds;

            Assert.AreEqual(expected, DateTimeUtils.ISOTimeToRippleTime("2024-11-03T08:30:00.000Z"));
        }

        [TestMethod]
        public void ISOTimeToRippleTime_String_IsIndependentOfTheLocalZone()
        {
            using LocalTimeZoneScope zone = LocalTimeZoneScope.Use(LocalTimeZoneScope.PlusFiveThirty);

            Assert.AreEqual(0, DateTimeUtils.ISOTimeToRippleTime("2000-01-01T00:00:00.000Z"));
            Assert.AreEqual(86_400, DateTimeUtils.ISOTimeToRippleTime("2000-01-02T00:00:00.000Z"));
        }

        [TestMethod]
        public void ISOTimeToRippleTime_LocalDate_IsConvertedToUtcFirst()
        {
            using LocalTimeZoneScope zone = LocalTimeZoneScope.Use(LocalTimeZoneScope.PlusFiveThirty);

            DateTime local = new DateTime(2000, 1, 1, 5, 30, 0, DateTimeKind.Local);

            Assert.AreEqual(0, DateTimeUtils.ISOTimeToRippleTime(local));
        }

        [TestMethod]
        public void ISOTimeToRippleTime_UnspecifiedDate_IsTakenAsUtc()
        {
            using LocalTimeZoneScope zone = LocalTimeZoneScope.Use(LocalTimeZoneScope.PlusFiveThirty);

            DateTime unspecified = new DateTime(2000, 1, 1, 5, 30, 0, DateTimeKind.Unspecified);

            Assert.AreEqual(5 * 3600 + 30 * 60, DateTimeUtils.ISOTimeToRippleTime(unspecified));
        }
    }
}
