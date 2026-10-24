using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;

using Xrpl.Amounts;

namespace XrplTests.Xrpl.Amounts;

/// <summary>
/// Results as text, so two of them compare equal when every public property does. A result's
/// <c>SnapshotAfter</c> is left out: reading it builds the next snapshot.
/// </summary>
internal static class DexResultText
{
    /// <summary>Every public property of a result, nested, as text: two results are the same when their texts are.</summary>
    internal static string Describe(object value)
    {
        StringBuilder text = new StringBuilder();
        Describe(value, text, 0);
        return text.ToString();
    }

    private static void Describe(object value, StringBuilder text, int depth)
    {
        if (value == null)
        {
            text.Append("null");
            return;
        }

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || depth > 6 ||
            type.GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType == type)
        {
            text.Append(value);
            return;
        }

        if (value is IEnumerable items)
        {
            text.Append('[');
            foreach (object item in items)
            {
                Describe(item, text, depth + 1);
                text.Append(", ");
            }

            text.Append(']');
            return;
        }

        text.Append('{');
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType != typeof(DexSnapshot)))
        {
            text.Append(property.Name).Append('=');
            Describe(property.GetValue(value), text, depth + 1);
            text.Append("; ");
        }

        text.Append('}');
    }
}
