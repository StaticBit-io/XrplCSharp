using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Tests;
using XrplTests;

using TimeoutException = Xrpl.Client.Exceptions.TimeoutException;

namespace XrplTests.Xrpl.Client;

/// <summary>
/// Tests for CancellationToken support in RequestManager and XrplClient.
/// Validates cancellation semantics, race conditions between Cancel/Timeout/Resolve,
/// backward compatibility with default token, and WebSocket connection isolation.
/// </summary>
[TestClass]
public class TestUCancellationToken
{
    #region Unit tests — RequestManager (no network)

    /// <summary>
    /// Verifies that cancelling a CancellationToken before the server responds
    /// causes the request Promise to throw OperationCanceledException.
    /// </summary>
    [TestMethod]
    public async Task Cancel_BeforeResolve_ThrowsOperationCanceled()
    {
        RequestManager rm = new RequestManager();
        using CancellationTokenSource cts = new CancellationTokenSource();
        Dictionary<string, dynamic> request = new Dictionary<string, dynamic> { ["command"] = "test" };

        RequestManager.XrplRequest xrplRequest = rm.CreateRequest(
            request, System.Threading.Timeout.InfiniteTimeSpan, cts.Token);

        cts.Cancel();

        await Helper.ThrowsExceptionAsync<OperationCanceledException>(
            () => xrplRequest.Promise);
    }

    /// <summary>
    /// Verifies that calling HandleResponse (Resolve) after the request was already
    /// cancelled does not throw. Confirms idempotent Resolve behavior.
    /// </summary>
    [TestMethod]
    public async Task Resolve_AfterCancel_IsIdempotent()
    {
        RequestManager rm = new RequestManager();
        using CancellationTokenSource cts = new CancellationTokenSource();
        Dictionary<string, dynamic> request = new Dictionary<string, dynamic> { ["command"] = "test" };

        RequestManager.XrplRequest xrplRequest = rm.CreateRequest(
            request, System.Threading.Timeout.InfiniteTimeSpan, cts.Token);

        cts.Cancel();
        await Task.Delay(50);

        string json = $"{{\"id\":\"{xrplRequest.Id}\",\"status\":\"success\",\"type\":\"response\",\"result\":{{}}}}";
        rm.HandleResponse(json);

        await Helper.ThrowsExceptionAsync<OperationCanceledException>(
            () => xrplRequest.Promise);
    }

    /// <summary>
    /// Verifies that if the server responds successfully before the token is cancelled,
    /// the Promise returns the result and the subsequent Cancel() is a no-op.
    /// </summary>
    [TestMethod]
    public async Task Resolve_BeforeCancel_ReturnsResult()
    {
        RequestManager rm = new RequestManager();
        using CancellationTokenSource cts = new CancellationTokenSource();
        Dictionary<string, dynamic> request = new Dictionary<string, dynamic> { ["command"] = "test" };

        RequestManager.XrplRequest xrplRequest = rm.CreateRequest(
            request, System.Threading.Timeout.InfiniteTimeSpan, cts.Token);

        string json = $"{{\"id\":\"{xrplRequest.Id}\",\"status\":\"success\",\"type\":\"response\",\"result\":{{\"value\":42}}}}";
        rm.HandleResponse(json);

        Dictionary<string, dynamic> result = await xrplRequest.Promise;
        Assert.IsNotNull(result);

        cts.Cancel();
    }

    /// <summary>
    /// Verifies that when both a timeout (5s) and a CancellationToken are active,
    /// cancelling the token first (after 50ms) produces OperationCanceledException,
    /// not TimeoutException. The first rejection wins.
    /// </summary>
    [TestMethod]
    public async Task Cancel_BeforeTimeout_GivesOperationCanceled()
    {
        RequestManager rm = new RequestManager();
        using CancellationTokenSource cts = new CancellationTokenSource();
        Dictionary<string, dynamic> request = new Dictionary<string, dynamic> { ["command"] = "test" };

        RequestManager.XrplRequest xrplRequest = rm.CreateRequest(
            request, TimeSpan.FromSeconds(5), cts.Token);

        cts.CancelAfter(50);

        Exception caught = null;
        try
        {
            await xrplRequest.Promise;
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(caught, "Expected an exception from the cancelled request");
        Assert.IsInstanceOfType(caught, typeof(OperationCanceledException),
            $"Expected OperationCanceledException but got {caught.GetType().Name}: {caught.Message}");
    }

    /// <summary>
    /// Verifies that when timeout (100ms) fires before the token is manually cancelled,
    /// the Promise throws TimeoutException. The subsequent cts.Cancel() is a no-op
    /// because Reject is idempotent.
    /// </summary>
    [TestMethod]
    public async Task Timeout_BeforeCancel_GivesTimeoutException()
    {
        RequestManager rm = new RequestManager();
        using CancellationTokenSource cts = new CancellationTokenSource();
        Dictionary<string, dynamic> request = new Dictionary<string, dynamic> { ["command"] = "test" };

        RequestManager.XrplRequest xrplRequest = rm.CreateRequest(
            request, TimeSpan.FromMilliseconds(100), cts.Token);

        Exception caught = null;
        try
        {
            await xrplRequest.Promise;
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(caught, "Expected a TimeoutException from the timed-out request");
        Assert.IsInstanceOfType(caught, typeof(TimeoutException),
            $"Expected TimeoutException but got {caught.GetType().Name}: {caught.Message}");

        cts.Cancel();
    }

    /// <summary>
    /// Verifies backward compatibility: creating a request without a CancellationToken
    /// (default) still times out with TimeoutException as before the CT feature was added.
    /// </summary>
    [TestMethod]
    public async Task DefaultToken_WorksAsBeforeTimeout()
    {
        RequestManager rm = new RequestManager();
        Dictionary<string, dynamic> request = new Dictionary<string, dynamic> { ["command"] = "test" };

        RequestManager.XrplRequest xrplRequest = rm.CreateRequest(
            request, TimeSpan.FromMilliseconds(100));

        await Helper.ThrowsExceptionAsync<TimeoutException>(
            () => xrplRequest.Promise);
    }

    /// <summary>
    /// Verifies that CancellationToken works correctly with the generic CreateGRequest path
    /// (typed request/response), not just the Dictionary-based CreateRequest.
    /// </summary>
    [TestMethod]
    public async Task CreateGRequest_Cancel_ThrowsOperationCanceled()
    {
        RequestManager rm = new RequestManager();
        using CancellationTokenSource cts = new CancellationTokenSource();
        FakeGRequest fakeRequest = new FakeGRequest { Command = "test_grequest" };

        RequestManager.XrplGRequest xrplRequest = rm.CreateGRequest<object, FakeGRequest>(
            fakeRequest, System.Threading.Timeout.InfiniteTimeSpan, cts.Token);

        cts.Cancel();

        await Helper.ThrowsExceptionAsync<OperationCanceledException>(
            () => xrplRequest.Promise);
    }

    #endregion

    #region E2E tests — MockRippled

    /// <summary>
    /// E2E test: verifies that cancelling a request via CancellationToken does not
    /// tear down the underlying WebSocket connection to the server.
    /// Uses a pre-cancelled token to guarantee cancellation fires before the response.
    /// </summary>
    [TestMethod]
    public async Task CancelledRequest_DoesNotBreakConnection()
    {
        SetupUnitClient runner = await new SetupUnitClient().SetupClient();
        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            Dictionary<string, dynamic> request = new Dictionary<string, dynamic>
            {
                ["command"] = "server_info"
            };

            try
            {
                await runner.client.connection.Request(request, cancellationToken: cts.Token);
                Assert.Fail("Expected OperationCanceledException");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.IsTrue(runner.client.IsConnected(),
                "Connection should remain open after request cancellation");
        }
        finally
        {
            await runner.client.Disconnect();
        }
    }

    /// <summary>
    /// E2E test: verifies that after a request is cancelled, the client can still
    /// successfully send and receive subsequent requests on the same connection.
    /// </summary>
    [TestMethod]
    public async Task NextRequest_AfterCancel_WorksNormally()
    {
        SetupUnitClient runner = await new SetupUnitClient().SetupClient();
        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            Dictionary<string, dynamic> cancelledRequest = new Dictionary<string, dynamic>
            {
                ["command"] = "server_info"
            };

            try
            {
                await runner.client.connection.Request(cancelledRequest, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
            }

            await Task.Delay(100);

            Dictionary<string, dynamic> normalRequest = new Dictionary<string, dynamic>
            {
                ["command"] = "server_info"
            };
            Dictionary<string, dynamic> result = await runner.client.connection.Request(normalRequest);
            Assert.IsNotNull(result, "Request after cancelled request should succeed");
        }
        finally
        {
            await runner.client.Disconnect();
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Minimal request DTO with an Id property discoverable via reflection,
    /// used by <see cref="RequestManager.CreateGRequest{T,R}"/> in tests.
    /// </summary>
    private class FakeGRequest
    {
        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("command")]
        public string Command { get; set; } = "fake";
    }

    #endregion
}
