using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// Replays the golden vectors rippled's own <c>STAmount</c>, <c>Quality</c> and <c>mulRatio</c>
/// code produced (<c>Fixtures/Amount</c>) through <see cref="XrplAmount"/>,
/// <see cref="XrplAmountMath"/>, <see cref="XrplQuality"/> and <see cref="OfferCrossing"/>, and
/// requires every result to match field for field: kind, sign, mantissa and exponent - and which
/// operations throw.
/// </summary>
[TestClass]
public class TestUAmountVectors
{
    private const string ResourceName = "XrplTests.Fixtures.Amount.vectors.txt.gz";

    private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };
    private static readonly IssuedCurrency Iou = new IssuedCurrency { Currency = "USD", Issuer = "rhub8VRN55s94qWKDv6jmDy1pUykJzF3wq" };
    private static readonly IssuedCurrency Mpt = new IssuedCurrency { MptIssuanceId = "00000001A407AF5856CCF3C42619DAA925813FC955C72983" };

    private static readonly Lazy<IReadOnlyList<string>> Vectors = new(LoadVectors);

    [TestMethod]
    public void TestVectorsCoverEveryScaleAndMode()
    {
        IReadOnlyList<string> lines = Vectors.Value;
        Assert.HasCount(8808, lines);

        CollectionAssert.AreEquivalent(
            new[] { "Small", "LargeLegacy", "Large320", "Large330" },
            lines.Select(line => line.Split(' ')[0]).Distinct().ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "ToNearest", "TowardsZero", "Downward", "Upward" },
            lines.Select(line => line.Split(' ')[1]).Distinct().ToArray());
    }

    [TestMethod]
    [DataRow("mulRound")]
    [DataRow("mulRoundStrict")]
    [DataRow("divRound")]
    [DataRow("divRoundStrict")]
    [DataRow("divide")]
    [DataRow("multiply")]
    [DataRow("add")]
    [DataRow("fromNumber")]
    [DataRow("getRate")]
    [DataRow("ceilIn")]
    [DataRow("ceilInStrict")]
    [DataRow("ceilOut")]
    [DataRow("ceilOutStrict")]
    [DataRow("mulRatio")]
    public void TestOperationMatchesRippled(string operation)
    {
        List<string> failures = new List<string>();
        int count = 0;
        foreach (string line in Vectors.Value)
        {
            string[] parts = line.Split(' ');
            if (!string.Equals(parts[3], operation, StringComparison.Ordinal))
                continue;

            count++;
            int equals = Array.IndexOf(parts, "=");
            string expected = string.Join(' ', parts.Skip(equals + 1));
            string actual = Evaluate(parts, equals);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                failures.Add($"{line}  -> got {actual}");
        }

        Assert.IsGreaterThan(0, count, $"no vectors for {operation}");
        Assert.IsEmpty(failures, $"{failures.Count} of {count} differ:\n" + string.Join('\n', failures.Take(25)));
    }

    private static string Evaluate(string[] parts, int equals)
    {
        LedgerRules rules = Rules(parts[0], parts[1], parts[2] == "1");
        NumberRounding ambient = rules.AmbientRounding;
        string[] args = parts.Skip(4).Take(equals - 4).ToArray();

        try
        {
            switch (parts[3])
            {
                case "mulRound":
                    return Format(XrplAmountMath.MulRound(Amount(args[0]), Amount(args[1]), Asset(args[2]), args[3] == "1", rules));
                case "mulRoundStrict":
                    return Format(XrplAmountMath.MulRoundStrict(Amount(args[0]), Amount(args[1]), Asset(args[2]), args[3] == "1", rules));
                case "divRound":
                    return Format(XrplAmountMath.DivRound(Amount(args[0]), Amount(args[1]), Asset(args[2]), args[3] == "1", rules));
                case "divRoundStrict":
                    return Format(XrplAmountMath.DivRoundStrict(Amount(args[0]), Amount(args[1]), Asset(args[2]), args[3] == "1", rules));
                case "divide":
                    return Format(XrplAmountMath.Divide(Amount(args[0]), Amount(args[1]), Asset(args[2]), rules));
                case "multiply":
                    return Format(XrplAmountMath.Multiply(Amount(args[0]), Amount(args[1]), Asset(args[2]), rules));
                case "add":
                    return Format(XrplAmountMath.Add(Amount(args[0]), Amount(args[1]), rules));
                case "fromNumber":
                    XrplNumber number = XrplNumber.Add(
                        XrplNumber.Zero,
                        new XrplNumber(long.Parse(args[1], CultureInfo.InvariantCulture), int.Parse(args[2], CultureInfo.InvariantCulture)),
                        rules.Context);
                    return Format(XrplAmount.FromNumber(Asset(args[0]), number, ambient));
                case "getRate":
                    return XrplQuality.FromAmounts(Amount(args[1]), Amount(args[0]), rules).Value.ToString(CultureInfo.InvariantCulture);
                case "ceilIn":
                    return Format(Quality(args[0]).CeilIn(Amount(args[1]), Amount(args[2]), Amount(args[3]), rules));
                case "ceilInStrict":
                    return Format(Quality(args[0]).CeilInStrict(Amount(args[1]), Amount(args[2]), Amount(args[3]), args[4] == "1", rules));
                case "ceilOut":
                    return Format(Quality(args[0]).CeilOut(Amount(args[1]), Amount(args[2]), Amount(args[3]), rules));
                case "ceilOutStrict":
                    return Format(Quality(args[0]).CeilOutStrict(Amount(args[1]), Amount(args[2]), Amount(args[3]), args[4] == "1", rules));
                case "mulRatio":
                    return Format(OfferCrossing.MulRatio(
                        Amount(args[0]),
                        uint.Parse(args[1], CultureInfo.InvariantCulture),
                        uint.Parse(args[2], CultureInfo.InvariantCulture),
                        args[3] == "1",
                        rules));
                default:
                    throw new InvalidOperationException($"unknown operation {parts[3]}");
            }
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException or ArgumentException)
        {
            return "!error";
        }
    }

    private static LedgerRules Rules(string scale, string mode, bool mpTokensV2) => new LedgerRules
    {
        LargeNumbers = scale != "Small",
        FixCleanup3_2_0 = scale is "Large320" or "Large330",
        FixCleanup3_3_0 = scale == "Large330",
        MPTokensV2 = mpTokensV2,
        AmbientRounding = Enum.Parse<NumberRounding>(mode),
    };

    private static XrplQuality Quality(string value) => new XrplQuality(ulong.Parse(value, CultureInfo.InvariantCulture));

    private static IssuedCurrency Asset(string kind) => kind switch
    {
        "X" => Xrp,
        "M" => Mpt,
        _ => Iou,
    };

    /// <summary>An amount from its raw STAmount fields, "K:[-]valueEoffset".</summary>
    private static XrplAmount Amount(string text)
    {
        string kind = text.Substring(0, 1);
        string rest = text.Substring(2);
        bool negative = rest.StartsWith('-');
        if (negative)
            rest = rest.Substring(1);

        int e = rest.IndexOf('e');
        BigInteger value = BigInteger.Parse(rest.Substring(0, e), CultureInfo.InvariantCulture);
        int offset = int.Parse(rest.Substring(e + 1), CultureInfo.InvariantCulture);
        IssuedCurrency asset = Asset(kind);
        return XrplAmount.Canonical(asset, XrplAmount.KindOf(asset), negative, value, offset, NumberRounding.ToNearest);
    }

    private static string Format(XrplAmount amount)
    {
        char kind = amount.Kind switch
        {
            AmountKind.Xrp => 'X',
            AmountKind.Mpt => 'M',
            _ => 'I',
        };
        return $"{kind}:{(amount.IsNegative ? "-" : string.Empty)}{amount.StMantissa}e{amount.StExponent}";
    }

    private static string Format((XrplAmount In, XrplAmount Out) amounts) => Format(amounts.In) + " " + Format(amounts.Out);

    private static IReadOnlyList<string> LoadVectors()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"missing embedded resource {ResourceName}");
        using GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress);
        using StreamReader reader = new StreamReader(gzip);
        List<string> lines = new List<string>();
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length > 0)
                lines.Add(line);
        }

        return lines;
    }
}
