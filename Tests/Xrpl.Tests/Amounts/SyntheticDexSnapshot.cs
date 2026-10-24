using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Xrpl.AddressCodec;
using Xrpl.Amounts;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// A snapshot of the size a busy pair's books read at depth: offer owners holding lines to a
/// handful of gateways, and two books per gateway (XRP for its USD and back), each in quality
/// order as <c>book_offers</c> returns it. Optionally an XRP/USD pool on the first gateway,
/// priced better than its books so a payment reaches it.
/// </summary>
internal static class SyntheticDexSnapshot
{
    /// <summary>A taker funded with XRP and holding a line to every gateway.</summary>
    internal static readonly string Alice = Address("alice");

    /// <summary>A destination holding a line to every gateway.</summary>
    internal static readonly string Bob = Address("bob");

    /// <summary>A taker funded with XRP and holding no trust line.</summary>
    internal static readonly string Dave = Address("dave");

    /// <summary>The XRP/USD pool's account on the first gateway.</summary>
    internal static readonly string Pool = Address("pool");

    internal static IssuedCurrency Xrp { get; } = new IssuedCurrency { Currency = "XRP" };

    /// <summary>A valid classic address made from a name, the same for the same name.</summary>
    internal static string Address(string name) =>
        XrplCodec.EncodeAccountID(SHA256.HashData(Encoding.UTF8.GetBytes(name)).Take(20).ToArray());

    internal static string Gateway(int index) => Address("gw" + index.ToString(CultureInfo.InvariantCulture));

    internal static string Owner(int index) => Address("owner" + index.ToString("D5", CultureInfo.InvariantCulture));

    internal static IssuedCurrency Usd(string issuer) => new IssuedCurrency { Currency = "USD", Issuer = issuer };

    internal static XrplAmount Iou(string issuer, string value) => XrplAmount.Parse(Usd(issuer), value);

    internal static XrplAmount Drops(ulong drops) => XrplAmount.Parse(Xrp, drops.ToString(CultureInfo.InvariantCulture));

    /// <param name="gateways">The issuers of USD; each has two books.</param>
    /// <param name="owners">The offer owners; each holds a line to every gateway.</param>
    /// <param name="offersPerBook">The offers in each book.</param>
    /// <param name="withPool">Whether an XRP/USD pool trades on the first gateway's books.</param>
    internal static DexSnapshot Build(int gateways = 5, int owners = 1000, int offersPerBook = 400, bool withPool = false)
    {
        List<DexAccount> accounts = new List<DexAccount>();
        List<DexTrustLine> lines = new List<DexTrustLine>();
        List<DexOffer> offers = new List<DexOffer>();

        for (int g = 0; g < gateways; g++)
            accounts.Add(new DexAccount { Address = Gateway(g), Balance = 100_000_000_000, DefaultRipple = true, Sequence = 1 });

        foreach (string taker in new[] { Alice, Bob })
        {
            accounts.Add(new DexAccount { Address = taker, Balance = 1_000_000_000_000, OwnerCount = (uint)gateways, Sequence = 1 });
            for (int g = 0; g < gateways; g++)
                lines.Add(Line(taker, Gateway(g), "0"));
        }

        accounts.Add(new DexAccount { Address = Dave, Balance = 1_000_000_000_000, Sequence = 1 });

        for (int o = 0; o < owners; o++)
        {
            string owner = Owner(o);
            accounts.Add(new DexAccount { Address = owner, Balance = 100_000_000_000, OwnerCount = 10, Sequence = 1 });
            for (int g = 0; g < gateways; g++)
                lines.Add(Line(owner, Gateway(g), "100000"));
        }

        int index = 0;
        for (int g = 0; g < gateways; g++)
        {
            string gateway = Gateway(g);
            for (int i = 0; i < offersPerBook; i++)
            {
                // Each offer a little worse than the one before it, so each book is already in quality
                // order; the owners sell USD from 1 XRP up and buy it from 0.99 XRP down, so the books
                // do not cross. At most 990 offers a book.
                XrplAmount usd = Iou(gateway, "100");
                offers.Add(Offer(index++, Owner(i % owners), Drops(1_000_000UL * (100 + (ulong)i)), usd));
                offers.Add(Offer(index++, Owner((i + 1) % owners), usd, Drops(99_000_000UL - 100_000UL * (ulong)i)));
            }
        }

        List<DexAmmPool> pools = new List<DexAmmPool>();
        if (withPool)
            pools.Add(new DexAmmPool { Account = Pool, Balance = Drops(10_000_000_000), Balance2 = Iou(Gateway(0), "20000"), TradingFee = 500 });

        return new DexSnapshot
        {
            ParentCloseTime = 800_000_000,
            LedgerSequence = 90_000_000,
            ReserveBase = 10_000_000,
            ReserveIncrement = 2_000_000,
            Accounts = accounts,
            TrustLines = lines,
            Offers = offers,
            Pools = pools,
        };
    }

    private static DexTrustLine Line(string holder, string issuer, string balance) =>
        new DexTrustLine
        {
            Account = holder,
            Balance = Iou(issuer, balance),
            Limit = Iou(issuer, "1000000000"),
            Reserve = true,
        };

    private static DexOffer Offer(int index, string owner, XrplAmount takerPays, XrplAmount takerGets) =>
        new DexOffer
        {
            Index = index.ToString("X64", CultureInfo.InvariantCulture),
            Account = owner,
            TakerPays = takerPays,
            TakerGets = takerGets,
            Quality = XrplQuality.FromAmounts(takerPays, takerGets),
        };
}
