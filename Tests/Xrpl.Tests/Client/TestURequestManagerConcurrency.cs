using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;

namespace Xrpl.Tests.ClientLib
{
    [TestClass]
    public class TestURequestManagerConcurrency
    {
        private class FakeRequest
        {
            public Guid? Id { get; set; }
        }

        private static void RunConcurrent(Action body, out IReadOnlyList<Exception> errors)
        {
            int threadCount = Math.Max(8, Environment.ProcessorCount * 2);
            const int iterationsPerThread = 500;

            ConcurrentBag<Exception> collected = new ConcurrentBag<Exception>();
            Barrier barrier = new Barrier(threadCount);
            Thread[] threads = new Thread[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        try
                        {
                            body();
                        }
                        catch (Exception ex)
                        {
                            collected.Add(ex);
                        }
                    }
                });
            }

            foreach (Thread thread in threads)
                thread.Start();
            foreach (Thread thread in threads)
                thread.Join();

            errors = collected.ToArray();
        }

        [TestMethod]
        public void CreateRequest_ConcurrentCalls_AssignUniqueIdsWithoutCollision()
        {
            RequestManager manager = new RequestManager();
            ConcurrentBag<Guid> ids = new ConcurrentBag<Guid>();

            RunConcurrent(
                () =>
                {
                    RequestManager.XrplRequest request = manager.CreateRequest(
                        new Dictionary<string, object>(),
                        System.Threading.Timeout.InfiniteTimeSpan);
                    ids.Add(request.Id);
                },
                out IReadOnlyList<Exception> errors);

            Assert.AreEqual(0, errors.Count,
                $"Concurrent CreateRequest threw {errors.Count} exception(s): " +
                string.Join(" | ", errors.Take(3).Select(e => e.Message)));

            List<Guid> all = ids.ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(),
                "Concurrent CreateRequest assigned duplicate ids");
        }

        [TestMethod]
        public void CreateGRequest_ConcurrentCalls_AssignUniqueIdsWithoutCollision()
        {
            RequestManager manager = new RequestManager();
            ConcurrentBag<Guid> ids = new ConcurrentBag<Guid>();

            RunConcurrent(
                () =>
                {
                    RequestManager.XrplGRequest request = manager.CreateGRequest<object, FakeRequest>(
                        new FakeRequest(),
                        System.Threading.Timeout.InfiniteTimeSpan);
                    ids.Add(request.Id);
                },
                out IReadOnlyList<Exception> errors);

            Assert.AreEqual(0, errors.Count,
                $"Concurrent CreateGRequest threw {errors.Count} exception(s): " +
                string.Join(" | ", errors.Take(3).Select(e => e.Message)));

            List<Guid> all = ids.ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(),
                "Concurrent CreateGRequest assigned duplicate ids");
        }
    }
}
