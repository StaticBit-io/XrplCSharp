namespace Xrpl.BinaryCodec.Enums
{
    public class XChainBridgeField : Field
    {
        public XChainBridgeField(string name, int nthOfType,
            bool isSigningField = true, bool isSerialised = true) :
                base(name, nthOfType, FieldType.XChainBridge,
                    isSigningField, isSerialised) { }
    }
}
