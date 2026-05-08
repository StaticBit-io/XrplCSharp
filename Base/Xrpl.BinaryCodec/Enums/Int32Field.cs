namespace Xrpl.BinaryCodec.Enums
{
    public class Int32Field : Field
    {
        public Int32Field(string name, int nthOfType,
            bool isSigningField = true, bool isSerialised = true) :
                base(name, nthOfType, FieldType.Int32,
                    isSigningField, isSerialised) { }
    }
}
