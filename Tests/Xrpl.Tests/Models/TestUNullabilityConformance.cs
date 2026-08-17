using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

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
    }
}
