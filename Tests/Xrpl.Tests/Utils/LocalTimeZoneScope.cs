using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Reflection;

namespace XrplTests.Xrpl.Utils
{
    /// <summary>
    /// Makes <see cref="TimeZoneInfo.Local"/> a chosen zone until disposed.
    /// </summary>
    /// <remarks>
    /// .NET offers no supported way to change the local zone inside a running process, so this sets
    /// the private cache <see cref="TimeZoneInfo.Local"/> reads from. The zone is process-wide: a test
    /// class using this must be <c>[DoNotParallelize]</c>. When the runtime's internals move, the
    /// test is reported inconclusive rather than run against the machine's own zone.
    /// </remarks>
    internal sealed class LocalTimeZoneScope : IDisposable
    {
        private readonly object _cachedData;
        private readonly FieldInfo _localField;

        private LocalTimeZoneScope(object cachedData, FieldInfo localField)
        {
            _cachedData = cachedData;
            _localField = localField;
        }

        public static LocalTimeZoneScope Use(TimeZoneInfo zone)
        {
            FieldInfo cachedDataField = typeof(TimeZoneInfo).GetField("s_cachedData", BindingFlags.NonPublic | BindingFlags.Static);
            object cachedData = cachedDataField?.GetValue(null);
            FieldInfo localField = cachedData?.GetType().GetField("_localTimeZone", BindingFlags.NonPublic | BindingFlags.Instance);
            if (localField is null)
                Assert.Inconclusive("TimeZoneInfo internals changed; cannot switch the local time zone.");

            localField.SetValue(cachedData, zone);
            return new LocalTimeZoneScope(cachedData, localField);
        }

        /// <summary>A fixed UTC+05:30 zone without daylight saving time.</summary>
        public static TimeZoneInfo PlusFiveThirty { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test+0530", TimeSpan.FromMinutes(330), "Test+0530", "Test+0530");

        /// <summary>
        /// UTC-08:00 with US daylight saving time (second Sunday of March to first Sunday of
        /// November, at 02:00), so the hour after 01:00 on the November change is ambiguous.
        /// </summary>
        public static TimeZoneInfo UsPacific { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "TestPacific",
            TimeSpan.FromHours(-8),
            "TestPacific",
            "TestPST",
            "TestPDT",
            new[]
            {
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    DateTime.MinValue.Date,
                    DateTime.MaxValue.Date,
                    TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                    TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday)),
            });

        public void Dispose()
        {
            // Null makes the next read of TimeZoneInfo.Local rebuild it from the machine.
            _localField.SetValue(_cachedData, null);
        }
    }
}
