using System.Threading.Tasks;

namespace Xrpl.Client
{
    /// <summary>
    /// Awaiting helpers for the <see cref="XrplResponse{T}"/> a client method returns.
    /// </summary>
    /// <remarks>
    /// Reading the projection off an awaited call otherwise reads
    /// <c>(await client.ServerFeatures()).Result</c>: the call has to be parenthesised so the
    /// member access lands on the awaited value rather than the task. That is unlike ordinary
    /// awaiting, and the member it reaches is spelled the same as <see cref="Task{TResult}.Result"/>
    /// - which blocks - so the line reads as sync-over-async to anyone scanning it, and to
    /// analyzers looking for exactly that shape. Neither is true here.
    /// <para>
    /// <c>await client.ServerFeatures().Typed()</c> puts the await back where it belongs. The
    /// three forms are equivalent; use whichever fits:
    /// <code>
    /// ServerFeatures f = await client.ServerFeatures().Typed();   // projection only
    /// var (f, raw)     = await client.ServerFeatures();           // both
    /// XrplResponse&lt;ServerFeatures&gt; r = await client.ServerFeatures();  // the envelope
    /// </code>
    /// </para>
    /// </remarks>
    public static class XrplResponseTaskExtensions
    {
        /// <summary>
        /// Awaits the call and hands back the typed projection alone.
        /// </summary>
        /// <remarks>
        /// The projection is lossy in both directions - see <see cref="XrplResponse{T}"/>. Anything
        /// that has to show or verify what the node actually said needs
        /// <see cref="XrplResponse{T}.Raw"/>, so await the call itself rather than using this.
        /// </remarks>
        public static async Task<T> Typed<T>(this Task<XrplResponse<T>> response)
            => (await response.ConfigureAwait(false)).Result;
    }
}
