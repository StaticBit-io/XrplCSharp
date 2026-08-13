using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Methods;

using Timer = System.Timers.Timer;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// A token that is already cancelled runs its registration callback inline, so the request is
    /// rejected in the middle of being built — before its timeout timer exists. These tests pin
    /// that nothing is left behind on that path: no timer in the timeout map, no pending promise.
    /// </summary>
    [TestClass]
    public class TestURequestManagerCancellation
    {
        private static ConcurrentDictionary<Guid, Timer> Timeouts(RequestManager manager)
        {
            FieldInfo field = typeof(RequestManager).GetField(
                "timeoutsAwaitingResponse",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "timeoutsAwaitingResponse is gone - update this test");
            return (ConcurrentDictionary<Guid, Timer>)field.GetValue(manager);
        }

        private static ConcurrentDictionary<Guid, TaskInfo> Promises(RequestManager manager)
        {
            FieldInfo field = typeof(RequestManager).GetField(
                "promisesAwaitingResponse",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "promisesAwaitingResponse is gone - update this test");
            return (ConcurrentDictionary<Guid, TaskInfo>)field.GetValue(manager);
        }

        [TestMethod]
        public async Task TestUCancelledTokenLeavesNoTimerBehindOnCreateRequest()
        {
            RequestManager manager = new RequestManager();
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            Dictionary<string, object> request = new Dictionary<string, object> { ["command"] = "ping" };
            RequestManager.XrplRequest created = manager.CreateRequest(
                request,
                TimeSpan.FromSeconds(30),
                cts.Token);

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => created.Promise);

            Assert.AreEqual(0, Timeouts(manager).Count, "timeout timer outlived the cancelled request");
            Assert.AreEqual(0, Promises(manager).Count, "promise outlived the cancelled request");
        }

        [TestMethod]
        public async Task TestUCancelledTokenLeavesNoTimerBehindOnCreateGRequest()
        {
            RequestManager manager = new RequestManager();
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            RequestManager.XrplGRequest created = manager.CreateGRequest<object, PingRequest>(
                new PingRequest(),
                TimeSpan.FromSeconds(30),
                cts.Token);

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => created.Promise);

            Assert.AreEqual(0, Timeouts(manager).Count, "timeout timer outlived the cancelled request");
            Assert.AreEqual(0, Promises(manager).Count, "promise outlived the cancelled request");
        }

        /// <summary>
        /// The plain path still registers a timer and still cleans it up once the request finishes,
        /// so the guard above cannot pass by never registering one in the first place.
        /// </summary>
        [TestMethod]
        public void TestULiveRequestRegistersAndThenReleasesItsTimer()
        {
            RequestManager manager = new RequestManager();

            Dictionary<string, object> request = new Dictionary<string, object> { ["command"] = "ping" };
            RequestManager.XrplRequest created = manager.CreateRequest(request, TimeSpan.FromSeconds(30));

            Assert.AreEqual(1, Timeouts(manager).Count, "a live request must arm its timeout");

            manager.Reject(created.Id, new OperationCanceledException("done"));

            Assert.AreEqual(0, Timeouts(manager).Count, "completing a request must release its timeout");
            Assert.AreEqual(0, Promises(manager).Count);
        }
    }
}
