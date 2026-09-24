using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;

namespace XrplTests.BinaryCodecLib.Types;

/// <summary>
/// Replays the golden vectors rippled's <c>Number.cpp</c> produced (<c>Fixtures/Number</c>) through
/// the C# port and requires every result to match, including which operations throw.
/// </summary>
[TestClass]
public class TestNumberVectors
{
    private const string ResourceName = "XrplTests.Fixtures.Number.vectors.txt.gz";

    private static readonly Lazy<IReadOnlyList<string>> Vectors = new(LoadVectors);

    [TestMethod]
    public void TestVectorsAreComplete()
    {
        IReadOnlyList<string> lines = Vectors.Value;
        Assert.HasCount(19150, lines);

        string[] scales = lines.Select(line => line.Split(' ')[0]).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(new[] { "Large320", "Large330", "LargeLegacy", "Small" }, scales);
    }

    [TestMethod]
    [DataRow("new")]
    [DataRow("newi")]
    [DataRow("add")]
    [DataRow("sub")]
    [DataRow("mul")]
    [DataRow("div")]
    [DataRow("int")]
    [DataRow("trunc")]
    [DataRow("pow")]
    [DataRow("root2")]
    [DataRow("root")]
    [DataRow("powf")]
    public void TestOperationMatchesRippled(string operation)
    {
        List<string> failures = new List<string>();
        int count = 0;
        foreach (string line in Vectors.Value)
        {
            string[] parts = line.Split(' ');
            if (!string.Equals(parts[2], operation, StringComparison.Ordinal))
                continue;

            count++;
            string expected = parts[^1];
            string actual = Evaluate(parts);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                failures.Add($"{line}  (got {actual})");
        }

        Assert.IsGreaterThan(0, count, $"No vectors for '{operation}'.");
        Assert.IsEmpty(failures, $"{failures.Count} of {count} '{operation}' vectors differ:{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(20))}");
    }

    private static string Evaluate(string[] parts)
    {
        NumberContext context = NumberContext.Create(
            Enum.Parse<NumberMantissaScale>(parts[0]),
            Enum.Parse<NumberRounding>(parts[1]));
        try
        {
            return parts[2] switch
            {
                "new" => Format(ParseWire(parts[3], context)),
                "newi" => Format(ParseInternal(parts[3], context)),
                "add" => Format(NumberCore.Add(ParseWire(parts[3], context), ParseWire(parts[4], context), context)),
                "sub" => Format(NumberCore.Subtract(ParseWire(parts[3], context), ParseWire(parts[4], context), context)),
                "mul" => Format(NumberCore.Multiply(ParseWire(parts[3], context), ParseWire(parts[4], context), context)),
                "div" => Format(NumberCore.Divide(ParseWire(parts[3], context), ParseWire(parts[4], context), context)),
                "int" => ParseWire(parts[3], context).ToInt64(context).ToString(CultureInfo.InvariantCulture),
                "trunc" => Format(ParseWire(parts[3], context).Truncate(context)),
                "pow" => Format(NumberCore.Power(ParseWire(parts[3], context), uint.Parse(parts[4], CultureInfo.InvariantCulture), context)),
                "root2" => Format(NumberCore.Root2(ParseWire(parts[3], context), context)),
                "root" => Format(NumberCore.Root(ParseWire(parts[3], context), uint.Parse(parts[4], CultureInfo.InvariantCulture), context)),
                "powf" => Format(NumberCore.Power(
                    ParseWire(parts[3], context),
                    uint.Parse(parts[4], CultureInfo.InvariantCulture),
                    uint.Parse(parts[5], CultureInfo.InvariantCulture),
                    context)),
                _ => throw new InvalidDataException($"Unknown operation '{parts[2]}'."),
            };
        }
        catch (OverflowException)
        {
            return "!overflow";
        }
        catch (Exception ex) when (ex is DivideByZeroException or ArgumentException or InvalidOperationException)
        {
            return "!error";
        }
    }

    private static NumberCore ParseWire(string text, NumberContext context)
    {
        if (text == "Z")
            return NumberCore.Zero;

        int e = text.IndexOf('e');
        return NumberCore.FromWire(
            long.Parse(text.AsSpan(0, e), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            context);
    }

    private static NumberCore ParseInternal(string text, NumberContext context)
    {
        bool negative = text[0] == '-';
        string body = negative ? text.Substring(1) : text;
        int e = body.IndexOf('e');
        return NumberCore.FromInternal(
            negative,
            ulong.Parse(body.AsSpan(0, e), NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(body.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            context);
    }

    private static string Format(NumberCore value) =>
        value.IsZero
            ? "Z"
            : value.ExternalMantissa.ToString(CultureInfo.InvariantCulture) + "e" + value.ExternalExponent.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> LoadVectors()
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        using GZipStream gzip = new GZipStream(resource, CompressionMode.Decompress);
        using StreamReader reader = new StreamReader(gzip, Encoding.ASCII);
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
