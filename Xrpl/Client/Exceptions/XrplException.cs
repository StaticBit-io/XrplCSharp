using System;
using System.Text.RegularExpressions;

using Xrpl.Models.Subscriptions;

namespace Xrpl.Client.Exceptions
{
    public class RippleException : Exception
    {
        public RippleException() { }

        public RippleException(string message) : base(message) { }
        public RippleException(string message, Exception? InnerException) : base(message, InnerException) { }
    }

    public class XrplException : Exception
    {
        //public readonly string message;
        //public readonly byte[]? data;

        //public XrplException(byte[]? data, string message = "")
        //{
        //    //this.name = this.constructor.name; 
        //    this.message = message;
        //    this.data = data;
        //}

        public XrplException() { }

        public XrplException(string message) : base(message) { }
        public XrplException(string message, Exception? InnerException) : base(message, InnerException) { }
    }

    /// <summary>
    /// Exception thrown when rippled responds with an Exception.
    /// </summary>
    public class RippledException : XrplException
    {
        public ErrorResponse Response { get; }

        public RippledException(string message, ErrorResponse response) : base(message)
        {
            Response = response;
        }
    }

    public static class RippledErrorParser
    {
        private static readonly Regex RippledLineRegex =
            new(@"^RippledError:\s*(?<msg>[^\r\n]+)",
                RegexOptions.Multiline | RegexOptions.Compiled);

        public static string? TryExtractRippledError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return null;

            var m = RippledLineRegex.Match(errorMessage);
            return m.Success ? m.Groups["msg"].Value.Trim() : errorMessage;
        }
    }

    /// <summary>
    /// Exception thrown when xrpl.js cannot specify Exception type.
    /// </summary>
    public class UnexpectedException : XrplException { }
    /// <summary>
    /// Exception thrown when xrpl.js has an Exception with connection to rippled.
    /// </summary>
    public class ConnectionException : XrplException
    {
        public ConnectionException(string message) : base(message)
        {
        }
    }
    /// <summary>
    /// Exception thrown when xrpl.js is not connected to rippled server.
    /// </summary>
    public class NotConnectedException : XrplException
    {
        /// <summary>
        /// The message used when none is given. A bare <c>throw new NotConnectedException()</c>
        /// used to carry the runtime's "Exception of type ... was thrown", which says nothing to a
        /// consumer that classifies failures by their text.
        /// </summary>
        public const string DefaultMessage =
            "The client is not connected to a server. Call Connect() first, or wait for the connection to be restored.";

        public NotConnectedException(string message = null) : base(message ?? DefaultMessage)
        {
        }

        /// <summary>
        /// Keeps the failure that caused this one.
        /// </summary>
        /// <remarks>
        /// A subtype that has a cause - the handler exception behind
        /// <see cref="ConnectHandlerFailedException"/> - has nowhere else to put it:
        /// <see cref="Exception.InnerException"/> has no setter, so it can only be passed up to
        /// <see cref="Exception"/> at construction.
        /// </remarks>
        public NotConnectedException(string message, Exception? innerException)
            : base(message ?? DefaultMessage, innerException)
        {
        }
    }
    /// <summary>
    /// The client is not connected because the consumer disconnected it.
    /// </summary>
    /// <remarks>
    /// Nothing is going to bring the connection back on its own: <c>Connect()</c> is the only way
    /// out. A consumer that reacts to a lost connection by failing over to another server must not
    /// do so here - the client is down because it was asked to be.
    /// </remarks>
    public class ClientDisconnectedException : NotConnectedException
    {
        public ClientDisconnectedException(string message = null) : base(message) { }
    }

    /// <summary>
    /// The reconnect loop spent its budget of attempts and stopped.
    /// </summary>
    /// <remarks>
    /// This is the outcome that means the endpoint is not answering, and the one where failing
    /// over to another server is the right reaction. It is reported only under
    /// <c>ConnectionOptions.StopAfterMaxAttempts</c>; without it the loop keeps trying and there is
    /// nothing to report.
    /// </remarks>
    public class ReconnectExhaustedException : NotConnectedException
    {
        /// <summary>How many attempts were made. Equal to <see cref="MaxAttempts"/>.</summary>
        public int Attempts { get; }

        /// <summary>The budget that was configured, <c>ConnectionOptions.MaxReconnectAttempts</c>.</summary>
        public int MaxAttempts { get; }

        public ReconnectExhaustedException(string message, int attempts, int maxAttempts)
            : base(message)
        {
            Attempts = attempts;
            MaxAttempts = maxAttempts;
        }
    }

    /// <summary>
    /// The request was refused at once because the client was not connected and the policy in force
    /// is <c>RequestFailurePolicy.ImmediateFail</c>.
    /// </summary>
    /// <remarks>
    /// This says nothing about the server: the connection was being rebuilt, and the caller asked
    /// not to wait. Retrying once connected is the reaction; failing over is not.
    /// </remarks>
    public class RequestRefusedException : NotConnectedException
    {
        public RequestRefusedException(string message = null) : base(message) { }
    }

    /// <summary>
    /// The client gave up because its own <c>OnConnected</c> handler kept failing.
    /// </summary>
    /// <remarks>
    /// The node answered and the socket opened; what failed is consumer code running on this side,
    /// so moving to another server would be moving away from a healthy endpoint. The handler's own
    /// failure is kept in <see cref="Exception.InnerException"/>.
    /// </remarks>
    public class ConnectHandlerFailedException : NotConnectedException
    {
        /// <summary>How many times in a row the handler failed before the client gave up.</summary>
        public int Failures { get; }

        public ConnectHandlerFailedException(string message, int failures, Exception? innerException = null)
            : base(message, innerException)
        {
            Failures = failures;
        }
    }

    /// <summary>
    /// There is no connection and no attempt to make one.
    /// </summary>
    /// <remarks>
    /// Distinct from every other outcome in this family: nothing failed, nothing gave up, and
    /// nothing is in progress - <c>Connect()</c> was never called, or the client has settled after
    /// stopping. Waiting would wait forever, which is why it is reported instead.
    /// </remarks>
    public class NotConnectingException : NotConnectedException
    {
        public NotConnectingException(string message = null) : base(message) { }
    }

    /// <summary>
    /// The operation was overtaken by a later one, which now owns the connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A transition of the connection has one owner. An operation that finds the connection has
    /// moved on stands down and reports this instead of returning success from a server the client
    /// is no longer on. The same is reported to a request that was in flight when a transition
    /// swept it.
    /// </para>
    /// <para>
    /// It derives from <see cref="OperationCanceledException"/> because that is what these paths
    /// have always thrown, so no existing <c>catch</c> and no task status changes. Two consequences
    /// are worth knowing:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// awaited directly, this exception - and therefore <see cref="Kind"/> - reaches the caller.
    /// Through <c>Task.WhenAll</c> it also reaches the caller, as long as none of the combined
    /// tasks faulted. If one did, <c>WhenAll</c> records the faults only and drops the
    /// cancellation: the supersession is then absent from the <c>await</c> and from
    /// <c>Task.Exception</c> alike. Await the operation itself when the outcome matters.
    /// </description></item>
    /// <item><description>
    /// <see cref="OperationCanceledException.CancellationToken"/> is
    /// <see cref="CancellationToken.None"/>: nobody cancelled anything, so a filter of the form
    /// <c>when (ex.CancellationToken == myToken)</c> does not match here.
    /// </description></item>
    /// </list>
    /// </remarks>
    public class ConnectionSupersededException : OperationCanceledException
    {
        /// <summary>What kind of operation took the connection over.</summary>
        public ConnectionTransitionKind Kind { get; }

        /// <summary>
        /// Where the operation that took over left the client, when that is known; otherwise
        /// <c>null</c>.
        /// </summary>
        public string? SupersededBy { get; }

        public ConnectionSupersededException(
            string message,
            ConnectionTransitionKind kind,
            string? supersededBy = null)
            : base(message)
        {
            Kind = kind;
            SupersededBy = supersededBy;
        }
    }

    /// <summary>
    /// Exception thrown when xrpl.js has disconnected from rippled server.
    /// </summary>
    public class DisconnectedException : XrplException
    {
        public DisconnectedException(string message) : base(message)
        {
        }

        public DisconnectedException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }
    /// <summary>
    /// Exception thrown when rippled is not initialized.
    /// </summary>
    public class RippledNotInitializedException : XrplException { }
    /// <summary>
    /// Exception thrown when a request to rippled times out.
    /// <para>
    /// IMPORTANT: this is <c>Xrpl.Client.Exceptions.TimeoutException</c>, NOT
    /// <see cref="System.TimeoutException"/>. It derives from <see cref="XrplException"/>, so a
    /// <c>catch (System.TimeoutException)</c> will NOT catch it. Catch this type (or its base
    /// <see cref="XrplException"/>) explicitly; if both <c>using System;</c> and
    /// <c>using Xrpl.Client.Exceptions;</c> are in scope, fully-qualify the type to avoid catching
    /// the wrong one.
    /// </para>
    /// </summary>
    public class TimeoutException : XrplException
    {
        public TimeoutException(string message, object data = null) : base(message)
        {

        }
    }
    /// <summary>
    /// Exception thrown when xrpl.js sees a response in the wrong format.
    /// </summary>
    public class ResponseFormatException : XrplException
    {
        public ResponseFormatException(string message, object data = null) : base(message)
        {
        }
    }
    /// <summary>
    /// Exception thrown when xrpl.js sees a malformed transaction.
    /// </summary>
    public class ValidationException : XrplException
    {
        public ValidationException(string message = null) : base(message)
        {
        }
        public ValidationException(string message, Exception? InnerException) : base(message, InnerException) { }
    }
    /// <summary>
    /// Exception thrown when a client cannot generate a wallet from the testnet/devnet
    /// faucets, or when the client cannot infer the faucet URL(i.e.when the Client
    /// is connected to mainnet).
    /// </summary>
    public class XRPLFaucetException : XrplException
    {
        public XRPLFaucetException(string message = null) : base(message)
        {
        }

        /// <summary>
        /// Keeps the failure that caused this one. A faucet call fails through the network, the
        /// JSON or the node, and without the cause the caller is left with a sentence.
        /// </summary>
        public XRPLFaucetException(string message, Exception? InnerException) : base(message, InnerException)
        {
        }
    }
    /// <summary>
    /// Exception thrown when xrpl.js cannot retrieve a transaction, ledger, account, etc.
    /// From rippled.
    /// </summary>
    public class NotFoundException : XrplException
    {
        public NotFoundException(string message = "Not Found") : base(message) { }
    }
}