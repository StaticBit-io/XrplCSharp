using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Threading.Tasks;

using Xrpl.Client;

using XrplTests;

namespace Xrpl.Tests
{
    /// <summary>
    /// Boundary checks for the health-check timing options. Both feed a timer directly —
    /// <c>HealthCheckInterval</c> is cast to an int of milliseconds on the WASM path, where zero
    /// fires once and never repeats and out-of-range values are rejected by the timer itself — so a
    /// bad value has to fail on the way in, naming the option, rather than quietly disabling the
    /// check that recovers dead connections.
    /// </summary>
    [TestClass]
    public class TestUHealthCheckOptions
    {
        private static XrplClient CreateClient(TimeSpan? healthCheckInterval = null, TimeSpan? inactivityTimeout = null) =>
            new XrplClient("ws://127.0.0.1:1", new XrplClient.ClientOptions
            {
                UseCustomPing = true,
                UseCheckHealth = true,
                HealthCheckInterval = healthCheckInterval ?? TimeSpan.FromSeconds(20),
                InactivityTimeout = inactivityTimeout ?? TimeSpan.FromSeconds(60),
                ConnectionAttemptTimeout = TimeSpan.FromSeconds(1),
                ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(1),
            });

        [TestMethod]
        public void TestZeroHealthCheckIntervalIsRejected()
        {
            // Validation runs while the connection is being constructed, so the bad value is rejected
            // before a client exists to connect with.
            ArgumentException error = Helper.ThrowsException<ArgumentException>(
                () => CreateClient(healthCheckInterval: TimeSpan.Zero));
            StringAssert.Contains(error.Message, "HealthCheckInterval");
        }

        [TestMethod]
        public void TestNegativeHealthCheckIntervalIsRejected()
        {
            ArgumentException error = Helper.ThrowsException<ArgumentException>(
                () => CreateClient(healthCheckInterval: TimeSpan.FromMilliseconds(-1)));
            StringAssert.Contains(error.Message, "HealthCheckInterval");
        }

        [TestMethod]
        public void TestOutOfRangeHealthCheckIntervalIsRejected()
        {
            // Past int.MaxValue milliseconds - the WASM timer cannot represent it
            ArgumentException error = Helper.ThrowsException<ArgumentException>(
                () => CreateClient(healthCheckInterval: TimeSpan.FromDays(30)));
            StringAssert.Contains(error.Message, "HealthCheckInterval");
        }

        [TestMethod]
        public void TestNonPositiveInactivityTimeoutIsRejected()
        {
            ArgumentException error = Helper.ThrowsException<ArgumentException>(
                () => CreateClient(inactivityTimeout: TimeSpan.Zero));
            StringAssert.Contains(error.Message, "InactivityTimeout");
        }

        /// <summary>
        /// The lower bound is 1ms, and the defaults must keep working — otherwise every existing
        /// consumer would start failing on connect.
        /// </summary>
        [TestMethod]
        public async Task TestBoundaryAndDefaultValuesAreAccepted()
        {
            // 1ms is the documented minimum: validation must let it through. Nothing is listening on
            // port 1, so the connect attempt fails on the transport - not on config validation.
            XrplClient atMinimum = CreateClient(
                healthCheckInterval: TimeSpan.FromMilliseconds(1),
                inactivityTimeout: TimeSpan.FromMilliseconds(1));

            Exception minimumError = await Helper.ThrowsExceptionAsync<Exception>(() => atMinimum.Connect());
            Assert.IsNotInstanceOfType(
                minimumError,
                typeof(ArgumentException),
                $"1ms should pass validation, but connect failed with: {minimumError.Message}");

            XrplClient atDefaults = CreateClient();
            Exception defaultError = await Helper.ThrowsExceptionAsync<Exception>(() => atDefaults.Connect());
            Assert.IsNotInstanceOfType(
                defaultError,
                typeof(ArgumentException),
                $"The default options should pass validation, but connect failed with: {defaultError.Message}");
        }
    }
}
