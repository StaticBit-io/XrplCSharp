namespace Xrpl.BinaryCodec.Enums
{
    public partial class EngineResult : SerializedEnumItem<byte>
    {
        public class EngineResultValues : SerializedEnumeration<EngineResult, byte>{}
        public static EngineResultValues Values = new EngineResultValues();
        private readonly string _description;
        public EngineResult(string name, int ordinal, string description = "") : base(name, ordinal)
        {
            _description = description;
        }
        private static EngineResult Add(string name, int ordinal, string description = "")
        {
            return Values.AddEnum(new EngineResult(name, ordinal, description));
        }

        public bool ShouldClaimFee()
        {
            return Ordinal >= 0;
        }
    }
}
