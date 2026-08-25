using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The limits a node puts on <c>Memos</c> before it will accept a transaction at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are local checks in rippled's <c>passesLocalChecks</c> → <c>isMemoOkay</c>
    /// (<c>src/libxrpl/protocol/STTx.cpp</c>). A transaction that fails them is not relayed, never
    /// reaches a ledger and costs no fee - but the consumer only finds out after building,
    /// autofilling and signing it, and the refusal does not say which field was at fault. Checking
    /// the same rules before signing turns that into an exception that names the problem.
    /// </para>
    /// <para>
    /// Two of the five rules the node applies are already enforced by the binary codec, so they are
    /// deliberately not repeated here: a member other than <c>MemoType</c>, <c>MemoData</c> or
    /// <c>MemoFormat</c> inside a <c>Memo</c> is refused when the blob is built, and so is a value
    /// that is not hex. Re-checking them would mean two places to keep in step with one truth.
    /// </para>
    /// </remarks>
    public static class MemoRules
    {
        /// <summary>
        /// The largest the serialized <c>Memos</c> array may be, in bytes.
        /// </summary>
        /// <remarks>
        /// The limit is on the array as a whole, so several memos do not raise it. In practice this
        /// leaves 1019 bytes of <c>MemoData</c> in a single memo carrying nothing else.
        /// </remarks>
        public const int MaxSerializedLength = 1024;

        /// <summary>
        /// Characters a decoded <c>MemoType</c> or <c>MemoFormat</c> may consist of - the ones RFC
        /// 3986 allows in a URL. <c>MemoData</c> is exempt: it carries arbitrary bytes.
        /// </summary>
        private const string UrlSafeCharacters =
            "0123456789" +
            "-._~:/?#[]@!$&'()*+,;=%" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "abcdefghijklmnopqrstuvwxyz";

        private static readonly bool[] AllowedInMemoType = BuildAllowedTable();

        /// <summary>
        /// Checks a transaction's <c>Memos</c> against the rules a node applies locally.
        /// </summary>
        /// <param name="memos">The value of the transaction's <c>Memos</c> field, or <c>null</c>.</param>
        /// <exception cref="ValidationException">
        /// When the array is too large, an element is not a <c>Memo</c> object, or a
        /// <c>MemoType</c>/<c>MemoFormat</c> decodes to something a URL may not contain.
        /// </exception>
        public static void Validate(object memos)
        {
            if (memos is null)
            {
                return;
            }

            JsonNode node = memos as JsonNode ?? JsonSerializer.SerializeToNode(memos, XrplJsonOptions.Default);

            // Anything that is not an array of well-formed Memo objects is refused by the codec a
            // moment later, and refused better: it names the member at fault. These rules only add
            // what nothing else checks, so a shape they cannot read is left alone rather than
            // reported here in poorer words.
            if (node is not JsonArray array || array.Count == 0)
            {
                return;
            }

            bool everyElementIsAMemo = true;
            foreach (JsonNode element in array)
            {
                JsonObject memo = TryUnwrapMemo(element);
                if (memo is null)
                {
                    everyElementIsAMemo = false;
                    continue;
                }

                ValidateUrlSafe(memo, "MemoType");
                ValidateUrlSafe(memo, "MemoFormat");
            }

            if (!everyElementIsAMemo)
            {
                return;
            }

            int length = SerializedLength(array);
            if (length < 0)
            {
                return;
            }

            if (length > MaxSerializedLength)
            {
                throw new ValidationException(
                    $"Memos: the serialized array is {length} bytes and a node accepts at most " +
                    $"{MaxSerializedLength}. The limit is on the whole array, so splitting the " +
                    $"content across several memos does not help.");
            }
        }

        /// <summary>
        /// Measures the <c>Memos</c> array the way the node measures it.
        /// </summary>
        /// <remarks>
        /// rippled serializes the array with <c>STArray::add</c>, which writes each element as its
        /// object start marker, the fields, and the object end marker - the array's own markers are
        /// not part of the length it then compares. <see cref="StArray.ToBytes"/> writes exactly
        /// that, which is why the measurement is taken here rather than reimplemented.
        /// </remarks>
        private static int SerializedLength(JsonArray array)
        {
            try
            {
                StArray serialized = StArray.FromJson(array);
                BytesList sink = new BytesList();
                serialized.ToBytes(sink);
                return sink.BytesLength();
            }
            catch (Exception)
            {
                // Content the codec cannot serialize at all - a value that is not hex, say. It
                // will refuse the transaction when it builds the blob and explain why; measuring
                // is not the place to find that out, and reporting it from here would replace a
                // precise message with a vague one.
                return -1;
            }
        }

        /// <summary>
        /// Returns the memo itself from an array element, or <c>null</c> when the element is not
        /// shaped like one.
        /// </summary>
        /// <remarks>
        /// On the wire a <c>Memos</c> element is an object with the single member <c>Memo</c> -
        /// rippled refuses an array holding anything else ("A memo array may contain only Memo
        /// objects"), and so does the codec, naming the member that does not belong. Anything else
        /// is therefore reported as <c>null</c> rather than thrown on.
        /// </remarks>
        private static JsonObject TryUnwrapMemo(JsonNode element)
        {
            if (element is JsonObject wrapper &&
                wrapper.Count == 1 &&
                wrapper.TryGetPropertyValue("Memo", out JsonNode inner) &&
                inner is JsonObject memo)
            {
                return memo;
            }

            return null;
        }

        private static void ValidateUrlSafe(JsonObject memo, string field)
        {
            if (!memo.TryGetPropertyValue(field, out JsonNode value) || value is null)
            {
                return;
            }

            string hex = value.GetValue<string>();
            if (string.IsNullOrEmpty(hex))
            {
                return;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromHexString(hex);
            }
            catch (FormatException)
            {
                // Left to the codec, which refuses non-hex when it builds the blob and says so in
                // its own words. Reporting it twice, differently, would only add a second story.
                return;
            }

            foreach (byte character in decoded)
            {
                if (!AllowedInMemoType[character])
                {
                    throw new ValidationException(
                        $"Memos: {field} decodes to a character a URL may not contain " +
                        $"(byte 0x{character:X2}). A node allows only alphanumerics and " +
                        $"-._~:/?#[]@!$&'()*+,;=% there; arbitrary bytes belong in MemoData.");
                }
            }
        }

        private static bool[] BuildAllowedTable()
        {
            bool[] allowed = new bool[256];
            foreach (char character in UrlSafeCharacters)
            {
                allowed[character] = true;
            }

            return allowed;
        }
    }
}
