using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Sugar;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// A request is released before its caller is woken, and the caller is not woken on the
    /// thread that handles the socket: a caller that reuses the request object (marker pagination
    /// does) must be able to send it again from its continuation.
    /// </summary>
    [TestClass]
    public class TestURequestManagerIdReuse
    {
        private static string SuccessMessage(Guid id) =>
            "{\"id\":\"" + id.ToString("D") + "\",\"status\":\"success\",\"type\":\"response\",\"result\":{\"ledger_index\":1}}";

        private static RequestManager.XrplGRequest Pending(RequestManager manager, LedgerDataRequest request) =>
            manager.CreateGRequest<LOLedgerData, LedgerDataRequest>(request, Timeout.InfiniteTimeSpan);

        [TestMethod]
        public void TestUResolve_DoesNotRunTheCallerOnTheReceiveThread()
        {
            RequestManager manager = new RequestManager();
            RequestManager.XrplGRequest pending = Pending(manager, new LedgerDataRequest());
            int receiveThread = Environment.CurrentManagedThreadId;

            Task<int> continuation = pending.Promise.ContinueWith(
                _ => Environment.CurrentManagedThreadId,
                TaskContinuationOptions.ExecuteSynchronously);

            manager.HandleResponse(SuccessMessage(pending.Id));

            Assert.IsTrue(continuation.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreNotEqual(receiveThread, continuation.Result);
        }

        [TestMethod]
        public void TestUResolve_TheSameRequestCanBeSentAgainFromTheContinuation()
        {
            RequestManager manager = new RequestManager();
            LedgerDataRequest request = new LedgerDataRequest();
            RequestManager.XrplGRequest first = Pending(manager, request);

            Task<RequestManager.XrplGRequest> next = first.Promise.ContinueWith(
                _ => Pending(manager, request),
                TaskContinuationOptions.ExecuteSynchronously);

            manager.HandleResponse(SuccessMessage(first.Id));

            Assert.IsTrue(next.Wait(TimeSpan.FromSeconds(5)), "the second request was refused");
            Assert.AreEqual(first.Id, next.Result.Id);
        }

        [TestMethod]
        public void TestUReject_TheSameRequestCanBeSentAgainFromTheContinuation()
        {
            RequestManager manager = new RequestManager();
            LedgerDataRequest request = new LedgerDataRequest();
            RequestManager.XrplGRequest first = Pending(manager, request);

            Task<RequestManager.XrplGRequest> next = first.Promise.ContinueWith(
                _ => Pending(manager, request),
                TaskContinuationOptions.ExecuteSynchronously);

            manager.Reject(first.Id, new XrplException("failed"));

            Assert.IsTrue(next.Wait(TimeSpan.FromSeconds(5)), "the second request was refused");
            Assert.AreEqual(first.Id, next.Result.Id);
        }

        [TestMethod]
        public async Task TestUGetBalances_SendsEachPageAsANewRequest()
        {
            List<object> idsSeen = new List<object>();
            IXrplClient client = PagedLinesClient.Create(idsSeen);

            List<Balance> balances = await client.GetBalances(
                "rPEPPER7kfTD9w2To4CQk6UCfuHM9c6GDY",
                new GetBalancesOptions { Peer = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" });

            Assert.AreEqual(3, idsSeen.Count);
            CollectionAssert.AllItemsAreUnique(idsSeen);
            Assert.AreEqual(3, balances.Count);
        }

        /// <summary>
        /// IXrplClient answering account_lines with three pages. Like the real client, it writes the
        /// id it chose into the request; a page that arrives with an id already set reuses it.
        /// </summary>
        public class PagedLinesClient : DispatchProxy
        {
            private List<object> _idsSeen;
            private int _page;

            public static IXrplClient Create(List<object> idsSeen)
            {
                IXrplClient proxy = Create<IXrplClient, PagedLinesClient>();
                ((PagedLinesClient)(object)proxy)._idsSeen = idsSeen;
                return proxy;
            }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name != nameof(IXrplClient.AccountLines))
                    throw new NotSupportedException(targetMethod?.Name);

                AccountLinesRequest request = (AccountLinesRequest)args[0];
                request.Id ??= Guid.NewGuid();
                _idsSeen.Add(request.Id);

                _page++;
                AccountLines lines = new AccountLines
                {
                    TrustLines = new List<TrustLine>
                    {
                        new TrustLine { Account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh", Currency = "USD", Balance = _page.ToString() },
                    },
                    Marker = _page < 3 ? "page" + _page : null,
                };
                return Task.FromResult(new XrplResponse<AccountLines>(lines, default, null, null, null, null, false));
            }
        }
    }
}
