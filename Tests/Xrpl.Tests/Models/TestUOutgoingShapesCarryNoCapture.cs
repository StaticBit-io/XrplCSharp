using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Unknown-field capture belongs on shapes read off a node, never on shapes sent to one.
    /// </summary>
    /// <remarks>
    /// The rule is not cosmetic. A type reachable from a request or a transaction is round-tripped
    /// back onto the wire, so a member captured from one node's response rides out inside a
    /// transaction the user never wrote - and <c>StObject.FromJson</c> passes <c>signingOnly</c>
    /// only to the top level, so a nested unknown member reaches the displayed <c>tx_json</c> but
    /// not the signed blob. Show one, sign another.
    /// <para>
    /// This is not hypothetical: <c>Common.PathStep</c> (reaching <c>Payment.Paths</c>),
    /// <c>AuthAccount</c> (<c>AMMBid</c>) and the <c>AuthorizeCredential*</c> pair
    /// (<c>DepositPreauth</c>) all carried capture until a review caught it. They were missed
    /// because the exclusion list was written by type name, while the property that matters is
    /// reachability from the request graph - which no naming convention expresses.
    /// </para>
    /// <para>
    /// Known limitation: properties typed as <c>object</c> are opaque to this walk. Nothing in the
    /// request graph is shaped that way today, but a future one would pass unchecked.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUOutgoingShapesCarryNoCapture
    {
        /// <summary>
        /// Whether instances of this type capture unknown members - declared here or inherited.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="BindingFlags.DeclaredOnly"/>. Capture arrives by inheritance
        /// far more often than by declaration: 47 method models get it from
        /// <see cref="Methods.BaseMethodResult"/> alone, and the defect that prompted this test was
        /// exactly that - the path step deriving from that base. A DeclaredOnly version of
        /// this check stayed green with the defect reintroduced.
        /// </remarks>
        private static bool CarriesCapture(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.GetCustomAttribute<JsonExtensionDataAttribute>(inherit: true) is not null);

        /// <summary>
        /// Every model type a property type can hold, unwrapping arrays and generics to any depth.
        /// </summary>
        /// <remarks>
        /// Recursive on purpose. <c>Payment.Paths</c> is <c>List&lt;List&lt;Path&gt;&gt;</c>: peeling
        /// one level yields <c>List&lt;Path&gt;</c>, which lives outside Xrpl.Models and gets
        /// discarded, so <c>PathStep</c> is never reached. A single-level version of this walk passed
        /// while <c>PathStep</c> carried capture - the very defect that prompted this test.
        /// </remarks>
        private static IEnumerable<Type> Unwrap(Type type)
        {
            if (type.IsArray)
            {
                Type element = type.GetElementType();
                if (element is not null)
                {
                    foreach (Type inner in Unwrap(element))
                    {
                        yield return inner;
                    }
                }
                yield break;
            }

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    foreach (Type inner in Unwrap(argument))
                    {
                        yield return inner;
                    }
                }
                yield break;
            }

            yield return type;
        }

        /// <summary>
        /// Every request class and every outgoing transaction, as the walk's starting points.
        /// </summary>
        /// <remarks>
        /// <see cref="ITransactionRequest"/>, not <see cref="ITransactionCommon"/>: response types
        /// implement Common too, so rooting the walk there drags in transaction metadata - the walk
        /// then reaches <c>ModifiedNode.FinalFields</c> and reports <c>BaseLedgerEntry</c>, which is
        /// a response shape doing exactly what it should. Confirmed by running it both ways: Common
        /// gives 207 roots and one false positive, Request gives 124 roots and none.
        /// </remarks>
        private static IEnumerable<Type> Roots(Assembly assembly) =>
            assembly.GetTypes().Where(t =>
                t.IsClass
                && !t.IsAbstract
                && t.Namespace is not null
                && t.Namespace.StartsWith("Xrpl.Models", StringComparison.Ordinal)
                && (t.Name.EndsWith("Request", StringComparison.Ordinal)
                    || typeof(ITransactionRequest).IsAssignableFrom(t)));

        [TestMethod]
        public void TestUNothingReachableFromARequestCapturesUnknownFields()
        {
            Assembly assembly = typeof(AccountInfoRequest).Assembly;

            Type[] roots = Roots(assembly).ToArray();
            // A walk that finds nothing proves nothing: this guards the discovery itself, so a
            // renamed interface or namespace fails loudly instead of turning the test green.
            Assert.IsTrue(roots.Length > 100,
                $"the walk found only {roots.Length} roots, against 124 when this was written - the request graph cannot have shrunk that far, so the discovery is broken rather than the models being clean");

            HashSet<Type> seen = new HashSet<Type>();
            Stack<Type> pending = new Stack<Type>(roots);
            Dictionary<Type, string> offenders = new Dictionary<Type, string>();

            while (pending.Count > 0)
            {
                Type current = pending.Pop();
                if (!seen.Add(current))
                {
                    continue;
                }

                if (CarriesCapture(current))
                {
                    offenders[current] = current.FullName;
                }

                foreach (PropertyInfo property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    foreach (Type candidate in Unwrap(property.PropertyType))
                    {
                        if (candidate.IsClass
                            && candidate.Namespace is not null
                            && candidate.Namespace.StartsWith("Xrpl.Models", StringComparison.Ordinal)
                            && !seen.Contains(candidate))
                        {
                            pending.Push(candidate);
                        }
                    }
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "these shapes are reachable from a request or a transaction and carry [JsonExtensionData]. "
                + "A member captured off a response would ride back out inside an outgoing transaction, "
                + "and signingOnly does not reach nested objects - so it would show in tx_json and be absent "
                + "from the signed blob:\n  "
                + string.Join("\n  ", offenders.Values.OrderBy(n => n, StringComparer.Ordinal)));
        }
    }
}
