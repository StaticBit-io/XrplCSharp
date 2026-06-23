# XrplCSharp

A pure C# implementation for interacting with the XRP Ledger. This library simplifies complex XRP Ledger operations including serialization, transaction signing, wallet management, and network communication.

## Packages

### [Xrpl.AddressCodec](reference/Xrpl.AddressCodec.html)

Functions for encoding and decoding XRP Ledger addresses and seeds.

- [XrplAddressCodec](reference/Xrpl.AddressCodec.XrplAddressCodec.html) - Main address encoding/decoding class
- [XrplCodec](reference/Xrpl.AddressCodec.XrplCodec.html) - Seed and key encoding utilities
- [B58](reference/Xrpl.AddressCodec.B58.html) - Base58 encoding implementation

### [Xrpl.BinaryCodec](reference/Xrpl.BinaryCodec.html)

Functions for encoding objects into the XRP Ledger's canonical binary format and decoding them.

- [XrplBinaryCodec](reference/Xrpl.BinaryCodec.XrplBinaryCodec.html) - Main binary codec class
- [Types](reference/Xrpl.BinaryCodec.Types.html) - Serializable type implementations
- [Binary](reference/Xrpl.BinaryCodec.Binary.html) - Binary parsing and serialization

### [Xrpl.Keypairs](reference/Xrpl.Keypairs.html)

Low-level functions for creating and using cryptographic keys with the XRP Ledger.

- [XrplKeypairs](reference/Xrpl.Keypairs.XrplKeypairs.html) - Key generation and signing
- [Ed25519](reference/Xrpl.Keypairs.Ed25519.html) - ED25519 algorithm implementation
- [K256](reference/Xrpl.Keypairs.K256.html) - SECP256K1 algorithm implementation

### [Xrpl.Client](reference/Xrpl.Client.html)

WebSocket client for communicating with XRP Ledger nodes.

- [XrplClient](reference/Xrpl.Client.XrplClient.html) - Main client class
- [Connection](reference/Xrpl.Client.connection.html) - Connection management
- [Models](reference/Xrpl.Models.html) - Request/Response models

### [Xrpl.X402](reference/Xrpl.X402.html)

x402 (HTTP-402) agentic payments client for the XRP Ledger — autonomously pay for HTTP resources in XRP or RLUSD. See the [x402 Agentic Payments Guide](X402-Guide.html).

- [X402PaymentHandler](reference/Xrpl.X402.X402PaymentHandler.html) - DelegatingHandler that pays 402 challenges
- [X402ClientOptions](reference/Xrpl.X402.X402ClientOptions.html) - Network, spending caps, intent binding
- [X402IntentBinding](reference/Xrpl.X402.X402IntentBinding.html) - InvoiceID / Memo binding modes

### [Xrpl.X402.AspNetCore](reference/Xrpl.X402.AspNetCore.html)

ASP.NET Core server middleware to require an x402 payment for an endpoint.

- [X402PaymentExtensions](reference/Xrpl.X402.AspNetCore.X402PaymentExtensions.html) - RequirePayment endpoint filter
- [LedgerSettlingFacilitator](reference/Xrpl.X402.AspNetCore.LedgerSettlingFacilitator.html) - settles locally
- [T54Facilitator](reference/Xrpl.X402.AspNetCore.T54Facilitator.html) - delegates to a t54 facilitator

## Quick Start

```csharp
using Xrpl.Client;

// Create and connect client
var client = new XrplClient("wss://s.altnet.rippletest.net:51233");
await client.Connect();

// Get account info
var response = await client.Request(new AccountInfoRequest 
{ 
    Account = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" 
});

// Disconnect when done
await client.Disconnect();
```

## Documentation

| Guide | EN | RU |
|-------|----|----|
| Connection Guide | [English](Connection-Guide.html) | [Русский](Connection-Guide.ru.html) |
| Error Classifier | [English](ErrorClassifier.html) | [Русский](ErrorClassifier.ru.html) |
| Cross-Chain Bridge | [English](XChainBridge-Guide.html) | [Русский](XChainBridge-Guide.ru.html) |
| Vault Guide | [English](Vault-Guide.html) | [Русский](Vault-Guide.ru.html) |
| Lending Protocol | [English](LendingProtocol-Guide.html) | [Русский](LendingProtocol-Guide.ru.html) |
| x402 Agentic Payments | [English](X402-Guide.html) | [Русский](X402-Guide.ru.html) |

- [API Reference](reference/Xrpl.Client.html) - Full API documentation
