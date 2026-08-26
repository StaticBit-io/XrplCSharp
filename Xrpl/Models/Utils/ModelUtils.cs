

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/utils/index.ts

using System;
using System.Collections.Generic;
using System.Linq;

namespace Xrpl.Models.Utils
{
    /// <summary>
    /// Helpers shared by the models.
    /// </summary>
    /// <remarks>
    /// Called <c>Index</c> until 11.0.0.0 - a calque of the barrel file <c>utils/index.ts</c> it was
    /// ported from, and a name that collides with <see cref="System.Index"/>, which is in scope in
    /// every file whether anyone asked for it or not. The class now matches the file it has always
    /// lived in.
    /// </remarks>
    public static class ModelUtils
    {
        /// <summary>
        /// Verify that all fields of an object are in fields.
        /// </summary>
        /// <param name="obj">Object to verify fields.</param>
        /// <param name="fields">Fields to verify</param>
        /// <returns>True if keys in object are all in fields.</returns>
        public static bool OnlyHasFields(this Dictionary<string, object> obj, string[] fields) => obj.Keys.All(key => fields.Contains(key));
        /// <summary>
        /// Perform bitwise AND (&amp;) to check if a flag is enabled within Flags (as a number).
        /// </summary>
        /// <param name="Flags"> A number that represents flags enabled.</param>
        /// <param name="checkFlag">A specific flag to check if it's enabled within Flags.</param>
        /// <returns>True if checkFlag is enabled within Flags.</returns>
        public static bool IsFlagEnabled(this uint Flags, uint checkFlag)
        {
            // eslint-disable-next-line no-bitwise -- flags needs bitwise
            return (checkFlag & Flags) == checkFlag;
        }
        /// <summary>
        /// Check if string is in hex format.
        /// </summary>
        /// <param name="str"> The string to check if it's in hex format.</param>
        /// <returns>True if string is in hex format</returns>
        public static bool IsHex(this string str) => System.Text.RegularExpressions.Regex.IsMatch(str, @"^[0-9A-Fa-f]+$");
    }
}
