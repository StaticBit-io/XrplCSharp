namespace Xrpl.BinaryCodec.Enums
{
    public class Hash192Field : Field {
        public Hash192Field(string name, int nthOfType,
            bool isSigningField = true, bool isSerialised = true) :
                base(name, nthOfType, FieldType.Hash192,
                    isSigningField, isSerialised) {}
    }
}