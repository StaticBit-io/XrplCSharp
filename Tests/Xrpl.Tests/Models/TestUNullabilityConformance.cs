using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Holds the models to what the protocol says can be absent. A non-nullable CLR property cannot
    /// express absence, so it re-serializes as a zero the node never sent — which is the whole
    /// defect this level exists to remove.
    /// </summary>
    /// <remarks>
    /// Two rules, and the second is broader than the first on purpose. rippled's requirement flag
    /// describes the ledger object; the same models also carry <c>PreviousFields</c>,
    /// <c>FinalFields</c> and <c>NewFields</c>, which are partial projections — <c>PreviousFields</c>
    /// holds only the members a transaction changed, so even a Required field can be missing there.
    /// </remarks>
    [TestClass]
    public class TestUNullabilityConformance
    {
        private static Dictionary<string, Type> Models()
        {
            FieldInfo field = typeof(TestULedgerEntryFieldsConformance)
                .GetField("Models", BindingFlags.NonPublic | BindingFlags.Static);

            // Reached by name, so a rename over there would otherwise surface here as a
            // NullReferenceException from an unrelated-looking test rather than as the real cause.
            Assert.IsNotNull(
                field,
                "TestULedgerEntryFieldsConformance no longer has a private static 'Models' field; "
                    + "this test reads it by name and has to be pointed at the new one.");
            return (Dictionary<string, Type>)field.GetValue(null);
        }

        private static PropertyInfo FindProperty(Type model, string protocolField)
        {
            foreach (PropertyInfo property in model.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                JsonPropertyNameAttribute name = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                string mapped = name?.Name ?? property.Name;
                if (string.Equals(mapped, protocolField, StringComparison.Ordinal))
                {
                    return property;
                }
            }

            return null;
        }

        private static bool CannotExpressAbsence(PropertyInfo property)
        {
            Type type = property.PropertyType;
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null;
        }

        /// <summary>
        /// A field rippled declares Optional or Default must map to a property that can be absent.
        /// Authoritative: the requirement comes from the vendored ledger_entries.macro.
        /// </summary>
        [TestMethod]
        public void TestUOptionalProtocolFieldsMapToNullableProperties()
        {
            Dictionary<string, Dictionary<string, RippledLedgerEntryFormats.Requirement>> formats =
                RippledLedgerEntryFormats.Parse();
            Dictionary<string, Type> models = Models();
            List<string> offenders = new List<string>();

            foreach (KeyValuePair<string, Type> pair in models.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!formats.TryGetValue(pair.Key, out Dictionary<string, RippledLedgerEntryFormats.Requirement> fields))
                {
                    continue;
                }

                foreach (KeyValuePair<string, RippledLedgerEntryFormats.Requirement> field in fields)
                {
                    if (field.Value == RippledLedgerEntryFormats.Requirement.Required)
                    {
                        continue;
                    }

                    PropertyInfo property = FindProperty(pair.Value, field.Key);
                    if (property is not null && CannotExpressAbsence(property))
                    {
                        offenders.Add($"{pair.Key}.{field.Key} is {field.Value} but {property.PropertyType.Name} cannot be absent");
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "a field the protocol allows to be absent must not re-serialize as a default:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// Every value-typed property of a ledger-entry model must be nullable, whatever the
        /// protocol says about the object itself.
        /// </summary>
        /// <remarks>
        /// Broader than the rule above because these models double as the contents of
        /// PreviousFields/FinalFields/NewFields. PreviousFields carries only what a transaction
        /// changed, so any member can be missing there and a non-nullable property fabricates a
        /// value for it — that is where the 156 invented members on a ten-entry account_tx came from.
        /// </remarks>
        [TestMethod]
        public void TestULedgerEntryPropertiesCanAllExpressAbsence()
        {
            List<string> offenders = new List<string>();

            foreach (KeyValuePair<string, Type> pair in Models().OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                foreach (PropertyInfo property in pair.Value.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                    {
                        continue;
                    }

                    if (CannotExpressAbsence(property))
                    {
                        offenders.Add($"{pair.Key}.{property.Name} : {property.PropertyType.Name}");
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "these appear in PreviousFields/FinalFields, where absence is normal:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// rippled TransactionType name -> the response model that carries its fields, built from
        /// the one place that registry actually exists at runtime.
        /// </summary>
        /// <remarks>
        /// There is no ready-made "tx type -> model" table in the repo (<see cref="TestUTxFormatConformance"/>
        /// compares <see cref="TxFormat"/> against <see cref="RippledTransactionFormats"/> - a table
        /// against a table, never touching a C# model). The only place a transaction name actually
        /// resolves to a model is the switch in <see cref="TransactionResponseConverter.Create"/>,
        /// which <see cref="Xrpl.Client.XrplClient"/> uses to deserialize every tx/account_tx/ledger
        /// response. Guessing the model from "&lt;TxName&gt;Response" by convention instead breaks
        /// silently on the two names that do not follow it - Clawback -&gt; ClawBackResponse,
        /// AMMClawback -&gt; AMMClawBackResponse - so this goes through the converter's own switch
        /// and fails outright if a type <see cref="TxFormat.Formats"/> declares has no response
        /// model wired into it, the same way a newly added ledger object would fail
        /// <see cref="TestULedgerEntryFieldsConformance"/> instead of being skipped silently.
        /// </remarks>
        private static Dictionary<string, Type> TransactionResponseModels()
        {
            Type unknownType = typeof(TransactionResponseConverter)
                .GetNestedType("TransactionResponseUnknown", BindingFlags.NonPublic);
            Assert.IsNotNull(unknownType, "TransactionResponseConverter no longer has a TransactionResponseUnknown sentinel - update this test's detection of unmapped types.");

            Dictionary<string, Type> models = new Dictionary<string, Type>(StringComparer.Ordinal);
            List<string> missing = new List<string>();

            foreach (Xrpl.BinaryCodec.Types.TransactionType transactionType in TxFormat.Formats.Keys)
            {
                string name = transactionType.ToString();
                object instance = TransactionResponseConverter.Create(name);
                Type model = instance.GetType();
                if (model == unknownType)
                {
                    missing.Add(name);
                    continue;
                }

                models[name] = model;
            }

            Assert.AreEqual(
                0,
                missing.Count,
                "TxFormat.Formats declares a transaction type with no response model registered in "
                    + "TransactionResponseConverter.Create - it would silently fall through to the "
                    + "unknown-type sentinel on every real response:"
                    + Environment.NewLine + string.Join(Environment.NewLine, missing));

            return models;
        }

        /// <summary>
        /// One documented exception, on the same footing as <c>NodeBase.LedgerEntryType</c> (see
        /// <c>TestULedgerEntryFieldsConformance</c>): a property the blanket scan flags but that is
        /// not actually a case of the defect this test exists to catch.
        /// </summary>
        /// <remarks>
        /// <see cref="TransactionResponse.TransactionType"/> is the wire discriminator
        /// <see cref="TransactionResponseConverter.Read"/> reads to pick which response subtype to
        /// construct in the first place - by the time this property is populated through the real
        /// deserialization path it can never be absent, unlike a PreviousFields-style reduced
        /// projection. Declared on <see cref="Xrpl.Models.Transactions.ITransactionCommon"/>, it is
        /// shared verbatim with <see cref="TransactionRequest"/> - the type actually on the signing
        /// path. Making it nullable would force the interface's declared type to change, which
        /// cascades into TransactionRequest and everywhere a transaction gets built and signed for
        /// submission, for a property that cannot legitimately go missing on the response side. That
        /// is a materially different risk profile from the 21 other properties this test fixed
        /// (each scoped to its own single-transaction-type interface), so it stays as a named
        /// exception instead of being converted.
        /// </remarks>
        private static bool IntentionallyNonNullable(PropertyInfo property)
        {
            return property.DeclaringType == typeof(TransactionResponse)
                && property.Name == nameof(TransactionResponse.TransactionType);
        }

        /// <summary>
        /// Every value-typed property of a transaction response model must be nullable, whatever
        /// TxFormat says about the transaction the model represents.
        /// </summary>
        /// <remarks>
        /// Response models are what the node actually sends back for tx/account_tx/ledger
        /// transactions, and nested transactions (e.g. Batch.RawTransactions) reuse them too, so a
        /// non-nullable property fabricates a value the node never sent - the same defect class
        /// <see cref="TestULedgerEntryPropertiesCanAllExpressAbsence"/> covers for ledger entries.
        /// <para>
        /// Grouped by <c>(DeclaringType, PropertyName)</c> rather than by <c>(model, property)</c>:
        /// most of these properties live on the shared <see cref="Transactions.TransactionResponse"/>
        /// base and would otherwise report once per every derived model that inherits them, which
        /// both inflates the apparent defect count and buries genuinely model-specific offenders
        /// under duplicate noise. <c>DeclaringType</c> is the one physical place a fix has to land.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void TestUTransactionResponsePropertiesCanAllExpressAbsence()
        {
            Dictionary<string, Type> models = TransactionResponseModels();

            Dictionary<(Type DeclaringType, string PropertyName), (PropertyInfo Property, HashSet<string> Models)> offenders =
                new Dictionary<(Type, string), (PropertyInfo, HashSet<string>)>();

            foreach (KeyValuePair<string, Type> pair in models.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                foreach (PropertyInfo property in pair.Value.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                    {
                        continue;
                    }

                    if (!CannotExpressAbsence(property))
                    {
                        continue;
                    }

                    if (IntentionallyNonNullable(property))
                    {
                        continue;
                    }

                    (Type DeclaringType, string PropertyName) key = (property.DeclaringType, property.Name);
                    if (!offenders.TryGetValue(key, out (PropertyInfo Property, HashSet<string> Models) entry))
                    {
                        entry = (property, new HashSet<string>(StringComparer.Ordinal));
                    }

                    entry.Models.Add(pair.Key);
                    offenders[key] = entry;
                }
            }

            int pairCount = offenders.Values.Sum(entry => entry.Models.Count);

            List<string> lines = offenders
                .OrderBy(o => o.Key.DeclaringType.FullName, StringComparer.Ordinal)
                .ThenBy(o => o.Key.PropertyName, StringComparer.Ordinal)
                .Select(o => $"{o.Key.DeclaringType.FullName}.{o.Key.PropertyName} : {o.Value.Property.PropertyType.Name}  ({o.Value.Models.Count} models)")
                .ToList();

            Assert.AreEqual(
                0,
                offenders.Count,
                $"these appear in transaction responses returned by the node, where absence is normal "
                    + $"({offenders.Count} unique properties covering {pairCount} model x property pairs):"
                    + Environment.NewLine + string.Join(Environment.NewLine, lines));
        }
    }
}
