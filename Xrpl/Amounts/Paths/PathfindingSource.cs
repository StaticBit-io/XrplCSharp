using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// The ledger a path search reads: accounts, their trust lines in the order the ledger keeps
    /// them, the index of order books, and the state the payment engine needs to rank the paths
    /// it finds. Built from a <see cref="DexSnapshot"/> or read from a node at one ledger.
    /// </summary>
    public abstract class PathfindingSource
    {
        private protected PathfindingSource(IBookIndex books, uint ledgerSequence, ulong reserveBase)
        {
            Books = books ?? throw new ArgumentNullException(nameof(books));
            LedgerSequence = ledgerSequence;
            ReserveBase = reserveBase;
        }

        /// <summary>The order books the search can use.</summary>
        public IBookIndex Books { get; }

        /// <summary>The ledger's sequence, which breaks ties between equally good accounts as the node does.</summary>
        public uint LedgerSequence { get; }

        /// <summary>The account reserve, in drops: an XRP payment must fund a new account with at least this.</summary>
        public ulong ReserveBase { get; }

        /// <summary>A search over a snapshot: its accounts and trust lines, and its books unless others are given.</summary>
        /// <param name="snapshot">The ledger state; its trust lines listed in the order they were created.</param>
        /// <param name="books">The order books; those of the snapshot's offers and pools when null.</param>
        /// <param name="ledgerSequence">The ledger's sequence, for the node's tie-break between accounts.</param>
        public static PathfindingSource FromSnapshot(DexSnapshot snapshot, IBookIndex books = null, uint ledgerSequence = 0)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            return new SnapshotSource(snapshot, books ?? BookIndex.FromSnapshot(snapshot), ledgerSequence);
        }

        /// <summary>
        /// A search against a node, at one ledger: accounts and trust lines read as the search
        /// reaches them, and the state for ranking read along the paths it finds.
        /// </summary>
        /// <param name="client">The node to read from.</param>
        /// <param name="books">The order books; see <see cref="BookIndex"/>.</param>
        /// <param name="ledger">The ledger to read; the last validated one when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static async Task<PathfindingSource> FromNodeAsync(
            IXrplClient client,
            IBookIndex books,
            LedgerIndex ledger = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            LOLedger header = await client
                .Ledger(new LedgerRequest { LedgerIndex = ledger ?? new LedgerIndex(LedgerIndexType.Validated) }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            LedgerEntity entity = header.LedgerEntity as LedgerEntity
                                  ?? throw new InvalidOperationException("The node returned no ledger header.");
            uint sequence = uint.Parse(entity.LedgerIndex, CultureInfo.InvariantCulture);
            LedgerIndex at = new LedgerIndex(sequence);
            (ulong reserveBase, _) = await DexSnapshot.ReservesAsync(client, at, cancellationToken).ConfigureAwait(false);
            return new NodeSource(client, books, sequence, reserveBase, at);
        }

        /// <summary>The account; null when it does not exist.</summary>
        internal abstract Task<DexAccount> AccountAsync(string address, CancellationToken cancellationToken);

        /// <summary>The account's trust lines, each from its side, in the order of its owner directory.</summary>
        internal abstract Task<IReadOnlyList<DexTrustLine>> LinesAsync(string account, CancellationToken cancellationToken);

        /// <summary>The trust line between the two accounts, from <paramref name="account"/>'s side; null when there is none.</summary>
        internal abstract Task<DexTrustLine> LineAsync(string account, string peer, string currency, CancellationToken cancellationToken);

        /// <summary>The state the engine reads to run <paramref name="requests"/> along <paramref name="paths"/> and the default path.</summary>
        internal abstract Task<DexSnapshot> StateAsync(
            IReadOnlyList<StrandBuilder.Request> requests,
            IReadOnlyList<IReadOnlyList<PathElement>> paths,
            CancellationToken cancellationToken);

        /// <summary>A trust line described from its other account.</summary>
        internal static DexTrustLine Flip(DexTrustLine line) =>
            new DexTrustLine
            {
                Account = line.Balance.Asset.Issuer,
                Balance = (-line.Balance).WithAsset(new IssuedCurrency { Currency = line.Balance.Asset.Currency, Issuer = line.Account }),
                Limit = line.PeerLimit?.WithAsset(new IssuedCurrency { Currency = line.Balance.Asset.Currency, Issuer = line.Account }),
                PeerLimit = line.Limit?.WithAsset(new IssuedCurrency { Currency = line.Balance.Asset.Currency, Issuer = line.Account }),
                QualityIn = line.PeerQualityIn,
                QualityOut = line.PeerQualityOut,
                PeerQualityIn = line.QualityIn,
                PeerQualityOut = line.QualityOut,
                NoRipple = line.PeerNoRipple,
                PeerNoRipple = line.NoRipple,
                Frozen = line.FrozenByAccount,
                FrozenByAccount = line.Frozen,
                DeepFrozen = line.DeepFrozen,
                Authorized = line.PeerAuthorized,
                PeerAuthorized = line.Authorized,
                Reserve = line.PeerReserve,
                PeerReserve = line.Reserve,
            };

        /// <summary>An account's trust lines from <c>account_lines</c>, every page, in the order the node returns them.</summary>
        internal static async Task<List<DexTrustLine>> AccountLinesAsync(IXrplClient client, string account, LedgerIndex at, CancellationToken cancellationToken)
        {
            List<DexTrustLine> result = new List<DexTrustLine>();
            object marker = null;
            do
            {
                AccountLines page;
                try
                {
                    page = await client
                        .AccountLines(new AccountLinesRequest(account) { LedgerIndex = at, Limit = 400, Marker = marker }, cancellationToken)
                        .Typed()
                        .ConfigureAwait(false);
                }
                catch (RippledException exception) when (exception.Response?.Error == "actNotFound")
                {
                    return result;
                }

                foreach (TrustLine line in page?.TrustLines ?? new List<TrustLine>())
                    result.Add(FromAccountLine(account, line));

                marker = page?.Marker switch
                {
                    null => null,
                    JsonElement { ValueKind: JsonValueKind.Null } => null,
                    object value => value,
                };
            }
            while (marker != null);

            return result;
        }

        private static DexTrustLine FromAccountLine(string account, TrustLine line)
        {
            IssuedCurrency asset = new IssuedCurrency { Currency = line.Currency, Issuer = line.Account };
            return new DexTrustLine
            {
                Account = account,
                Balance = XrplAmount.Parse(asset, line.Balance),
                Limit = XrplAmount.Parse(asset, line.Limit),
                PeerLimit = XrplAmount.Parse(asset, line.LimitPeer),
                QualityIn = line.QualityIn,
                QualityOut = line.QualityOut,
                NoRipple = line.NoRipple == true,
                PeerNoRipple = line.NoRipplePeer == true,
                FrozenByAccount = line.Freeze == true,
                Frozen = line.FreezePeer == true,
                DeepFrozen = line.DeepFreeze == true || line.DeepFreezePeer == true,
                Authorized = line.Authorized == true,
                PeerAuthorized = line.PeerAuthorized == true,
            };
        }

        private sealed class SnapshotSource : PathfindingSource
        {
            private readonly DexSnapshot _snapshot;
            private readonly Dictionary<string, DexAccount> _accounts;

            internal SnapshotSource(DexSnapshot snapshot, IBookIndex books, uint ledgerSequence)
                : base(books, ledgerSequence, snapshot.ReserveBase)
            {
                _snapshot = snapshot;
                _accounts = (snapshot.Accounts ?? Array.Empty<DexAccount>())
                    .Where(a => a?.Address != null)
                    .GroupBy(a => a.Address, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            }

            internal override Task<DexAccount> AccountAsync(string address, CancellationToken cancellationToken) =>
                Task.FromResult(_accounts.TryGetValue(address, out DexAccount account) ? account : null);

            internal override Task<IReadOnlyList<DexTrustLine>> LinesAsync(string account, CancellationToken cancellationToken)
            {
                List<DexTrustLine> lines = new List<DexTrustLine>();
                foreach (DexTrustLine line in _snapshot.TrustLines ?? Array.Empty<DexTrustLine>())
                {
                    if (line?.Account == null)
                        continue;
                    if (string.Equals(line.Account, account, StringComparison.Ordinal))
                        lines.Add(line);
                    else if (string.Equals(line.Balance.Asset.Issuer, account, StringComparison.Ordinal))
                        lines.Add(Flip(line));
                }

                return Task.FromResult<IReadOnlyList<DexTrustLine>>(lines);
            }

            internal override async Task<DexTrustLine> LineAsync(string account, string peer, string currency, CancellationToken cancellationToken)
            {
                string key = PathCurrency.Normalize(currency);
                foreach (DexTrustLine line in await LinesAsync(account, cancellationToken).ConfigureAwait(false))
                {
                    if (string.Equals(line.Balance.Asset.Issuer, peer, StringComparison.Ordinal) &&
                        PathCurrency.Normalize(line.Balance.Asset.Currency) == key)
                    {
                        return line;
                    }
                }

                return null;
            }

            internal override Task<DexSnapshot> StateAsync(
                IReadOnlyList<StrandBuilder.Request> requests,
                IReadOnlyList<IReadOnlyList<PathElement>> paths,
                CancellationToken cancellationToken) =>
                Task.FromResult(_snapshot);
        }

        private sealed class NodeSource : PathfindingSource
        {
            private readonly IXrplClient _client;
            private readonly LedgerIndex _at;
            private readonly Dictionary<string, DexAccount> _accounts = new Dictionary<string, DexAccount>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<DexTrustLine>> _lines = new Dictionary<string, List<DexTrustLine>>(StringComparer.Ordinal);

            internal NodeSource(IXrplClient client, IBookIndex books, uint ledgerSequence, ulong reserveBase, LedgerIndex at)
                : base(books, ledgerSequence, reserveBase)
            {
                _client = client;
                _at = at;
            }

            internal override async Task<DexAccount> AccountAsync(string address, CancellationToken cancellationToken)
            {
                if (!_accounts.TryGetValue(address, out DexAccount account))
                    _accounts[address] = account = await DexSnapshot.AccountAsync(_client, address, _at, cancellationToken).ConfigureAwait(false);

                return account;
            }

            internal override async Task<IReadOnlyList<DexTrustLine>> LinesAsync(string account, CancellationToken cancellationToken)
            {
                if (!_lines.TryGetValue(account, out List<DexTrustLine> lines))
                    _lines[account] = lines = await AccountLinesAsync(_client, account, _at, cancellationToken).ConfigureAwait(false);

                return lines;
            }

            internal override async Task<DexTrustLine> LineAsync(string account, string peer, string currency, CancellationToken cancellationToken)
            {
                // A peer's lines already read hold the line; otherwise one ledger_entry reads it.
                string key = PathCurrency.Normalize(currency);
                foreach (string holder in new[] { account, peer })
                {
                    if (!_lines.TryGetValue(holder, out List<DexTrustLine> lines))
                        continue;

                    string other = holder == account ? peer : account;
                    foreach (DexTrustLine line in lines)
                    {
                        if (string.Equals(line.Balance.Asset.Issuer, other, StringComparison.Ordinal) &&
                            PathCurrency.Normalize(line.Balance.Asset.Currency) == key)
                        {
                            return holder == account ? line : Flip(line);
                        }
                    }

                    return null;
                }

                return await DexSnapshot.LineAsync(_client, account, peer, currency, _at, cancellationToken).ConfigureAwait(false);
            }

            internal override Task<DexSnapshot> StateAsync(
                IReadOnlyList<StrandBuilder.Request> requests,
                IReadOnlyList<IReadOnlyList<PathElement>> paths,
                CancellationToken cancellationToken) =>
                DexSnapshot.ForPathsAsync(_client, requests, paths, addDefaultPath: true, _at, cancellationToken);
        }
    }
}
