using System;

namespace Xrpl.AddressCodec
{
    public class AddressCodecException : Exception
    {
        public AddressCodecException() { }

        public AddressCodecException(string message) : base(message){ }

        public AddressCodecException(string message, Exception innerException) : base(message, innerException) { }
    }
}
