using System;
using System.Collections.Generic;
using System.Globalization;

using Xrpl.BinaryCodec.Types;

namespace Xrpl.BinaryCodec.Enums
{
    /// <summary>
    /// Maps a Delegate permission between the name rippled reports and the number it serializes.
    /// </summary>
    /// <remarks>
    /// A permission is either a granular one - <see cref="GranularPermission"/>, values above
    /// <see cref="ushort.MaxValue"/> - or a transaction type, whose permission value is the
    /// transaction type code plus one. rippled's <c>STUInt32::getJson</c> emits the name whenever
    /// it can resolve one and the number otherwise, and its parser accepts both forms, so the
    /// codec has to understand both as well.
    /// <para>
    /// The equivalent in xrpl.js is the <c>delegatablePermissions</c> lookup that
    /// <c>XrplDefinitionsBase</c> associates with the PermissionValue field.
    /// </para>
    /// </remarks>
    public static class DelegatablePermissions
    {
        /// <summary>
        /// The value rippled treats as no permission at all, and therefore the one a name this
        /// version cannot map is reported as.
        /// </summary>
        public const uint UnknownPermissionValue = 0;

        private static readonly Lazy<Dictionary<string, uint>> ByName = new(BuildByName);
        private static readonly Lazy<Dictionary<uint, string>> ByValue = new(BuildByValue);

        private static Dictionary<string, uint> BuildByName()
        {
            Dictionary<string, uint> byName = new(StringComparer.Ordinal);

            // Enum.GetValues<T>() is .NET 5+, and this package targets netstandard2.0.
            foreach (GranularPermission permission in (GranularPermission[])Enum.GetValues(typeof(GranularPermission)))
                byName[permission.ToString()] = (uint)permission;

            foreach (TransactionType transactionType in TransactionType.Values)
            {
                // Invalid is defined with ordinal -1 and has no permission value.
                if (transactionType.Ordinal < 0)
                    continue;

                byName[transactionType.Name] = (uint)transactionType.Ordinal + 1;
            }

            return byName;
        }

        private static Dictionary<uint, string> BuildByValue()
        {
            Dictionary<uint, string> byValue = new();

            foreach (KeyValuePair<string, uint> entry in ByName.Value)
                byValue[entry.Value] = entry.Key;

            return byValue;
        }

        /// <summary>
        /// Maps a permission name to its numeric value.
        /// </summary>
        /// <param name="name">A granular permission name or a transaction type name.</param>
        /// <param name="value">The numeric value, when the name is known to this version.</param>
        /// <returns>True when the name was mapped.</returns>
        public static bool TryGetValue(string name, out uint value)
        {
            if (name is not null && ByName.Value.TryGetValue(name, out value))
                return true;

            value = UnknownPermissionValue;
            return false;
        }

        /// <summary>
        /// Maps a numeric permission value back to the name rippled uses for it.
        /// </summary>
        /// <param name="value">The numeric permission value.</param>
        /// <param name="name">The permission name, when the value is known to this version.</param>
        /// <returns>True when the value was mapped.</returns>
        public static bool TryGetName(uint value, out string name)
        {
            if (value == UnknownPermissionValue)
            {
                name = null;
                return false;
            }

            return ByValue.Value.TryGetValue(value, out name);
        }

        /// <summary>
        /// Reads a PermissionValue token in either of the forms rippled accepts: a name, a number,
        /// or a number written as a string.
        /// </summary>
        /// <param name="token">The token as it appears in JSON.</param>
        /// <param name="value">The numeric value the token denotes.</param>
        /// <returns>False when the token is a name this version cannot map.</returns>
        public static bool TryParse(string token, out uint value)
        {
            if (uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                return true;

            return TryGetValue(token, out value);
        }
    }
}
