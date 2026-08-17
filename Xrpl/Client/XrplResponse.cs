using System;
using System.Collections.Generic;

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
            IReadOnlyList<RippleResponseWarning> warnings,
            bool forwarded)
        {
            Result = result;
            Raw = raw;
            ApiVersion = apiVersion;
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
    }
}
