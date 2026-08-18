using System;
using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Subscriptions;

namespace Xrpl.Client
{
    /// <summary>
    /// A response from a node: the typed projection of its <c>result</c>, and the bytes that
    /// projection was made from.
    /// </summary>
    /// <remarks>
    /// The pair exists because the projection is lossy in both directions and cannot be turned
    /// back into what arrived: members the model does not know are dropped, and non-nullable CLR
    /// properties re-serialize as zeros the node never sent. Anything that has to show or verify
    /// what a node actually said — a wallet rendering a transaction for signing — reads
    /// <see cref="Raw"/>; everything else reads <see cref="Result"/>.
    /// <para>
    /// There is deliberately no implicit conversion to <typeparamref name="T"/>. Measured against
    /// this codebase it would carry fewer than half the call sites — those with an explicit type;
    /// the ones using <c>var</c> break regardless — leaving a partial compatibility that is harder
    /// to migrate than a clean break, and it would hide that <see cref="Raw"/> exists at all.
    /// </para>
    /// </remarks>
    public readonly struct XrplResponse<T>
    {
        private readonly IReadOnlyList<RippleResponseWarning> _warnings;

        /// <summary>Pairs a typed result with the envelope it was read from.</summary>
        public XrplResponse(
            T result,
            RawJson raw,
            uint? apiVersion,
            string status,
            string warning,
            IReadOnlyList<RippleResponseWarning> warnings,
            bool forwarded)
        {
            Result = result;
            Raw = raw;
            ApiVersion = apiVersion;
            Status = status;
            Warning = warning;
            _warnings = warnings;
            Forwarded = forwarded;
        }

        /// <summary>The <c>result</c> member, projected onto the requested type.</summary>
        public T Result { get; }

        /// <summary>
        /// The <c>result</c> member exactly as the node sent it. Empty when the response carried
        /// none.
        /// </summary>
        public RawJson Raw { get; }

        /// <summary>The API version the node answered on, when it reported one.</summary>
        public uint? ApiVersion { get; }

        /// <summary>
        /// <c>"success"</c>, or <c>"error"</c> if the request caused one.
        /// </summary>
        /// <remarks>
        /// A separate member for the same reason <see cref="Warning"/> is: <c>status</c> sits
        /// beside <c>result</c> in the envelope, not inside it, so it is not reachable through
        /// <see cref="Raw"/> — <see cref="Raw"/> is a slice of <c>result</c> alone.
        /// </remarks>
        public string Status { get; }

        /// <summary>
        /// The node's rate-limit signal, when it sent one — the literal <c>"load"</c>, meaning this
        /// client is approaching the threshold at which the server will disconnect it.
        /// </summary>
        /// <remarks>
        /// A separate member from <see cref="Warnings"/> because rippled reports it separately, and
        /// it is not reachable through <see cref="Raw"/> either: that is the <c>result</c> member,
        /// while this lives in the envelope around it.
        /// </remarks>
        public string Warning { get; }

        /// <summary>Warnings the node attached to this response. Never null.</summary>
        /// <remarks>
        /// rippled attaches these under load and on a reporting-mode server. Before this type they
        /// did not reach the caller at all — the envelope was unwrapped and discarded.
        /// </remarks>
        public IReadOnlyList<RippleResponseWarning> Warnings => _warnings ?? Array.Empty<RippleResponseWarning>();

        /// <summary>
        /// True when a Reporting Mode server forwarded this request to a P2P server and back.
        /// </summary>
        public bool Forwarded { get; }

        /// <summary>
        /// True when the node reported a <c>marker</c>, meaning more pages follow.
        /// </summary>
        /// <remarks>
        /// The extension on <see cref="BaseResponse"/> is no longer reachable from here — a caller
        /// holds this type now, not the envelope — and paging is the case this whole change was
        /// made for.
        /// </remarks>
        public bool HasNextPage => Raw.HasTopLevelProperty("marker"u8);

        /// <summary>
        /// Lets a caller take both halves at once: <c>var (info, raw) = await client.AccountInfo(request)</c>.
        /// </summary>
        /// <remarks>
        /// The call sites that broke hardest on this change are the ones using <c>var</c>, and this
        /// is what makes them a one-line edit rather than a restructure.
        /// </remarks>
        public void Deconstruct(out T result, out RawJson raw)
        {
            result = Result;
            raw = Raw;
        }
    }

    /// <summary>
    /// Builds <see cref="XrplResponse{T}"/> from what a resolved request left in its promise.
    /// </summary>
    public static class XrplResponse
    {
        /// <summary>
        /// Unpacks the pair the request manager put into the promise.
        /// </summary>
        /// <remarks>
        /// The manager knows the target type only as a <see cref="System.Type"/>, so it cannot
        /// build the generic response itself and carries both halves instead. This is where they
        /// come back together — inside the connection, so that no public method hands out an
        /// object the caller has no type to name.
        /// <para>
        /// Public because <see cref="ResolvedResponse"/> is: a caller working directly against
        /// <see cref="RequestManager"/> — rather than through the client's own methods, which
        /// already return <see cref="XrplResponse{T}"/> — gets a <c>Promise</c> that resolves to a
        /// <see cref="ResolvedResponse"/>, and this is the supported way to turn that back into a
        /// typed <see cref="XrplResponse{T}"/>.
        /// </para>
        /// </remarks>
        public static XrplResponse<T> From<T>(object resolved)
        {
            if (resolved is not ResolvedResponse carried)
            {
                throw new XrplException(
                    $"A resolved request carried {resolved?.GetType().Name ?? "null"} instead of its response envelope.");
            }

            return From<T>(carried);
        }

        /// <summary>
        /// Unpacks a <see cref="ResolvedResponse"/> already in hand.
        /// </summary>
        /// <remarks>
        /// The <c>object</c> overload exists because <c>Promise</c> is typed <see
        /// cref="System.Threading.Tasks.Task{TResult}">Task&lt;object&gt;</see> and cannot hand out
        /// anything more specific. A caller that already has the <see cref="ResolvedResponse"/> —
        /// having awaited the promise itself — should use this overload instead: the mismatch this
        /// type carries the most (a <c>Promise</c> that resolved to something other than what the
        /// request was created with) becomes a compile error here rather than the
        /// <see cref="XrplException"/> the other overload has to throw at run time.
        /// </remarks>
        public static XrplResponse<T> From<T>(ResolvedResponse resolved)
        {
            BaseResponse envelope = resolved.Envelope;

            return new XrplResponse<T>(
                (T)resolved.Result,
                envelope?.RawResult ?? default,
                envelope?.ApiVersion,
                envelope?.Status,
                envelope?.Warning,
                envelope?.Warnings,
                envelope?.Forwarded ?? false);
        }
    }
}
