using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using TxFormat = Xrpl.Models.Transaction.TxFormat;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Holds <see cref="TxFormat"/> to the field sets rippled declares in the vendored
    /// <c>transactions.macro</c>.
    /// </summary>
    /// <remarks>
    /// TxFormat is inert at runtime — <c>TxFormat.Validate</c> is not on the signing path, the
    /// binary codec serializes straight from definitions.json — so a wrong entry produces no
    /// symptom and nothing else in the suite notices. That is how three Check formats stayed
    /// verbatim copies of PaymentChannelClaim, and how a top-level WalletLocator sat on
    /// SignerListSet. This test is the only thing standing between that table and rot.
    ///
    /// It deliberately compares against a PINNED copy of the macro, not the live develop branch:
    /// upstream drift is protocol-watch's job (transactions.macro is in its watch list), and a
    /// network-dependent test would turn red on Ripple's release schedule rather than on ours.
    /// </remarks>
    [TestClass]
    public class TestUTxFormatConformance
    {
        /// <summary>
        /// Fields shared by every transaction, declared once in the TxFormat constructor.
        /// rippled keeps them in a separate commonFields list, so they are excluded on both sides.
        /// </summary>
        private static HashSet<string> CommonFieldNames() =>
            new TxFormat().Keys.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        [TestMethod]
        public void TestUTxFormat_MatchesRippledTransactionsMacro()
        {
            Dictionary<string, Dictionary<string, TxFormat.Requirement>> upstream = RippledTransactionFormats.Parse();
            HashSet<string> common = CommonFieldNames();
            StringBuilder report = new StringBuilder();

            foreach (KeyValuePair<BinaryCodec.Types.TransactionType, TxFormat> entry in TxFormat.Formats)
            {
                string name = entry.Key.Name;
                if (!upstream.TryGetValue(name, out Dictionary<string, TxFormat.Requirement> declared))
                {
                    report.AppendLine($"{name}: present in TxFormat, absent from transactions.macro");
                    continue;
                }

                Dictionary<string, TxFormat.Requirement> mine = entry.Value
                    .Where(field => !common.Contains(field.Key.Name))
                    .ToDictionary(field => field.Key.Name, field => field.Value, StringComparer.Ordinal);

                foreach (string field in declared.Keys.Except(mine.Keys).OrderBy(f => f, StringComparer.Ordinal))
                {
                    report.AppendLine($"{name}.{field}: declared by rippled, missing from TxFormat");
                }

                foreach (string field in mine.Keys.Except(declared.Keys).OrderBy(f => f, StringComparer.Ordinal))
                {
                    report.AppendLine($"{name}.{field}: in TxFormat, not a field of this transaction in rippled");
                }

                foreach (string field in declared.Keys.Intersect(mine.Keys).OrderBy(f => f, StringComparer.Ordinal))
                {
                    if (declared[field] != mine[field])
                    {
                        report.AppendLine($"{name}.{field}: rippled={declared[field]}, TxFormat={mine[field]}");
                    }
                }
            }

            foreach (string name in upstream.Keys
                         .Except(TxFormat.Formats.Keys.Select(type => type.Name))
                         .OrderBy(n => n, StringComparer.Ordinal))
            {
                report.AppendLine($"{name}: declared by rippled, absent from TxFormat");
            }

            Assert.AreEqual(
                string.Empty,
                report.ToString(),
                $"TxFormat diverges from the pinned rippled transactions.macro:{Environment.NewLine}{report}");
        }

        [TestMethod]
        public void TestUTxFormatConformance_ParserFailsLoudlyOnAnEmptySource()
        {
            // Guards the guard: a parser that quietly returns nothing would make the
            // conformance test above pass against an empty table.
            Dictionary<string, Dictionary<string, TxFormat.Requirement>> parsed = RippledTransactionFormats.Parse();

            Assert.IsGreaterThan(60, parsed.Count, "the vendored macro must yield a full format table");
            Assert.IsTrue(parsed.ContainsKey("Payment"), "Payment must be present — the parser matched nothing sane");
            Assert.IsTrue(parsed["Payment"].ContainsKey("Destination"));
            Assert.AreEqual(TxFormat.Requirement.Required, parsed["Payment"]["Destination"]);
        }
    }
}
