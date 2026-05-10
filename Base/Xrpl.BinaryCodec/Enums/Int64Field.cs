namespace Xrpl.BinaryCodec.Enums
{
    public class Int64Field : Field
    {
        public Int64Field(string name, int nthOfType,
            bool isSigningField = true, bool isSerialised = true) :
                base(name, nthOfType, FieldType.Int64,
                    isSigningField, isSerialised) { }
    }
}
