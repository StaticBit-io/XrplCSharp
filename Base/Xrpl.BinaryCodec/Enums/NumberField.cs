namespace Xrpl.BinaryCodec.Enums
{
    public class NumberField : Field
    {
        public NumberField(string name, int nthOfType,
            bool isSigningField = true, bool isSerialised = true) :
                base(name, nthOfType, FieldType.Number,
                    isSigningField, isSerialised) { }
    }
}
