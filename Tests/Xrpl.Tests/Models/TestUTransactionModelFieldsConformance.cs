using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Transactions;

using TxFormat = Xrpl.Models.Transaction.TxFormat;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Holds the transaction models to the field sets rippled declares in the vendored
    /// <c>transactions.macro</c>: every wire property a model declares must be a field of
    /// that transaction type.
    /// </summary>
    /// <remarks>
    /// The models-side counterpart of <see cref="TestUTxFormatConformance"/>, which checks the
    /// inert TxFormat table and says nothing about what a caller can actually set. A property
    /// the protocol does not define is worse than a missing one: the codec serializes it, the
    /// node refuses the whole transaction with <c>invalidTransaction</c>, and the error names a
    /// field the caller was invited to provide. That is how <c>VaultCreate.Amount</c>, left over
    /// from an early XLS-65 draft, shipped until 11.8.0.
    /// </remarks>
    [TestClass]
    public class TestUTransactionModelFieldsConformance
    {
        private static HashSet<string> CommonFieldNames() =>
            RippledTransactionFormats.CommonFields()
                .Select(field => field.Name)
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// The JSON names of the wire properties a model declares itself. Inherited properties
        /// belong to the common transaction fields or to the response envelope and are covered
        /// elsewhere; <see cref="JsonIgnoreAttribute"/> marks computed helpers that never reach
        /// the wire.
        /// </summary>
        private static IEnumerable<string> DeclaredWireNames(Type model)
        {
            foreach (PropertyInfo property in model.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    continue;

                if (property.GetCustomAttribute<JsonExtensionDataAttribute>() != null)
                    continue;

                yield return property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            }
        }

        private static Dictionary<string, Type> ModelsByName(Type baseType) =>
            typeof(TransactionRequest).Assembly.GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type) && type != baseType)
                .ToDictionary(type => type.Name, StringComparer.Ordinal);

        [TestMethod]
        public void TestUTransactionModels_DeclareOnlyProtocolFields()
        {
            Dictionary<string, Dictionary<string, TxFormat.Requirement>> upstream = RippledTransactionFormats.Parse();
            HashSet<string> common = CommonFieldNames();
            Dictionary<string, Type> requests = ModelsByName(typeof(TransactionRequest));
            Dictionary<string, Type> responses = ModelsByName(typeof(TransactionResponse));
            StringBuilder report = new StringBuilder();
            int checkedModels = 0;

            foreach (KeyValuePair<string, Dictionary<string, TxFormat.Requirement>> transaction
                     in upstream.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                List<Type> models = new List<Type>();
                if (requests.TryGetValue(transaction.Key, out Type request))
                    models.Add(request);
                if (responses.TryGetValue(transaction.Key + "Response", out Type response))
                    models.Add(response);

                foreach (Type model in models)
                {
                    checkedModels++;
                    foreach (string name in DeclaredWireNames(model).OrderBy(n => n, StringComparer.Ordinal))
                    {
                        if (transaction.Value.ContainsKey(name) || common.Contains(name))
                            continue;

                        report.AppendLine($"{model.Name}.{name}: on the model, not a field of {transaction.Key} in rippled");
                    }
                }
            }

            Assert.IsGreaterThanOrEqualTo(100, checkedModels, "the request and response models must be found by name");
            Assert.AreEqual(
                string.Empty,
                report.ToString(),
                $"Transaction models declare fields rippled does not ({RippledTransactionFormats.FixturePath}):{Environment.NewLine}{report}");
        }
    }
}
