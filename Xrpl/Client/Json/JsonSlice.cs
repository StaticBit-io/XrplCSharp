namespace Xrpl.Client.Json
{
    /// <summary>
    /// Where a JSON token sits inside the buffer it was read from, as a byte offset and length.
    /// Carries no reference to the buffer: the envelope that owns the frame pairs the two.
    /// </summary>
    public readonly struct JsonSlice
    {
        /// <summary>Byte offset of the first character of the token within the buffer.</summary>
        public int Offset { get; }

        /// <summary>Length of the token in bytes.</summary>
        public int Length { get; }

        /// <summary>True when no token was recorded — the member was absent from the buffer.</summary>
        public bool IsEmpty => Length == 0;

        public JsonSlice(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }
    }
}
