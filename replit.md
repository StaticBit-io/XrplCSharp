# XrplCSharp

## Overview

XrplCSharp is a pure C# implementation for interacting with the XRP Ledger. The library simplifies complex XRP Ledger operations including serialization, transaction signing, wallet management, and network communication. It provides a comprehensive SDK for building applications on the XRP Ledger using C# and .NET technologies.

The library is built on .NET 6.0 and follows a modular architecture with separate packages for different concerns (address encoding, binary codec, keypairs, and the main client library).

## User Preferences

Preferred communication style: Simple, everyday language.

## System Architecture

### Modular Package Design

**Problem**: Need to provide a comprehensive XRP Ledger SDK while maintaining separation of concerns and reusability.

**Solution**: Monorepo structure with distinct packages:
- `Xrpl.AddressCodec` - Address and seed encoding/decoding
- `Xrpl.BinaryCodec` - Binary serialization for XRP Ledger format
- `Xrpl.Keypairs` - Cryptographic key generation and management
- `Xrpl` - Main client library integrating all components

**Rationale**: This modular approach allows developers to use only the components they need while enabling the full SDK to leverage all modules. Each package has a focused responsibility making the codebase more maintainable.

### WebSocket-Based Network Client

**Problem**: Need real-time communication with XRP Ledger nodes for transaction submission and account monitoring with robust connection stability and error handling.

**Solution**: WebSocket client (`XrplClient`) for persistent connections to rippled nodes with async/await pattern for C# idiomatic usage.

**Features**:
- Connection event handling (`OnConnected`, `OnDisconnect`)
- Async request/response pattern
- Support for testnet and mainnet endpoints
- Intelligent reconnection logic with `ShouldReconnect()` method
- Detailed connection closure diagnostics via `DescribeClose()`
- Rate-limit detection and handling for XRPL server responses ("slowDown", "tooBusy")
- Real WebSocket close status and description propagation

**Recent Changes (November 2025)**:
- Updated `OnDisconnect` delegate signature to include both close code (`int?`) and description (`string?`) for better debugging
- Modified `WebSocketClient` to extract and propagate real `CloseStatus` and `CloseStatusDescription` from WebSocket close events
- Implemented XRPL-specific rate-limit error detection in message responses
- Added `DescribeClose()` method to classify close reasons (normal, error, rate-limit, fatal)
- Added `ShouldReconnect()` logic to prevent reconnection loops on fatal errors while maintaining retry behavior for transient failures
- **Fixed `State()` method** (November 12, 2025): Changed from incorrectly returning `Open` when WebSocket object exists to correctly returning actual WebSocket state (`ws?.State ?? Closed`). This fixes `IsConnected()` accuracy and enables proper `Connecting` state detection in reconnection logic.
- **Fixed initial connection failure handling** (November 12, 2025): Added automatic reconnection when first connection attempt fails (timeout, network unavailable, server not responding). Previously only established connections would auto-reconnect on failure.
- **Fixed connection timeout timer cleanup** (November 12, 2025): Timer now properly stops and disposes in `OnConnectionFailed()` to prevent perpetual connect/disconnect cycles after successful reconnection.
- **Fixed duplicate disconnect notifications** (November 12, 2025): Removed premature `CallOnDisconnected()` from `DisconnectAsync()` in `WebSocketClient`, ensuring disconnect event fires only once when WebSocket actually closes. Changed `OnceClose()` to pass `reasonText` (guaranteed non-empty) instead of raw `description` to prevent empty disconnect reasons.

### Wallet Management

**Problem**: Securely generate and manage XRP Ledger wallets with public/private keypairs.

**Solution**: `XrplWallet` class supporting:
- Random wallet generation
- Deterministic wallet creation from seeds
- Key derivation for ED25519 and SECP256K1 algorithms
- Address encoding/decoding using custom Base58 implementation

**Security Consideration**: Private keys are handled in-memory and should be properly secured by consuming applications.

### Binary Codec System

**Problem**: XRP Ledger uses a canonical binary format for transaction signing and hashing that differs from JSON representation.

**Solution**: Comprehensive binary codec with type system:
- Field type enumeration and metadata
- Binary parser/serializer for bidirectional conversion
- Type-specific handlers (Amount, Hash256, AccountID, etc.)
- Buffer management with `BytesList` and `BufferParser`

**Design Pattern**: Uses serialization/deserialization delegates and type registry pattern for extensibility.

### Cryptographic Operations

**Problem**: Support multiple signature algorithms (ED25519, SECP256K1) required by XRP Ledger.

**Solution**: Algorithm abstraction with implementations for both signature schemes, delegating to established cryptographic libraries (Chaos.NaCl.Standard for ED25519).

### Address Encoding Scheme

**Problem**: XRP Ledger uses custom Base58 encoding with checksums for addresses and seeds.

**Solution**: Custom Base58 codec (`B58`) with:
- Version prefixes for different identifier types
- Checksum validation
- Support for classic addresses and X-addresses

### Documentation System

**Problem**: Need comprehensive API documentation for developers.

**Solution**: DocFX-based documentation generation from XML comments in source code, producing static HTML documentation with cross-references.

## External Dependencies

### NuGet Packages

- **Chaos.NaCl.Standard** (v1.0.0) - ED25519 cryptographic operations
- **Microsoft.CSharp** (v4.7.0) - Dynamic language features
- **Microsoft.Extensions.Logging.Abstractions** (v1.0.0) - Logging abstraction layer

### Development Tools

- **.NET 6.0** - Runtime and SDK
- **Visual Studio 13.4.1+** - IDE with linting support
- **DocFX** (v2.59.4.0) - Documentation generation

### XRP Ledger Infrastructure

- **rippled nodes** - Connection via WebSocket (wss://)
  - Testnet: `wss://s.altnet.rippletest.net:51233`
  - Production nodes accessible via standard endpoints
- **Docker container** (`xrpllabsofficial/xrpld:1.12.0`) - For integration testing with standalone mode

### Testing Infrastructure

- **Unit Tests** - Filter: `TestU`
- **Integration Tests** - Filter: `TestI`, requires local or Docker-based rippled node
- **Blazor WebAssembly Test Application** (`Tests/TestsClients/Blazor-WebAssembly/`)
  - Interactive UI for testing XRP Ledger connectivity and features
  - Server selection dropdown (Mainnet/Testnet/Devnet/Custom)
  - Real-time connection status display showing current connected server
  - Account transaction loading functionality
  - **Server Selection Feature** (November 12, 2025):
    - Dropdown menu for quick switching between predefined networks
    - Custom URL input for connecting to private/local rippled nodes
    - "Change Server" functionality that triggers disconnect → reconnect sequence
    - CurrentServerUrl synchronized with live connection via `client.connection.GetUrl()`
    - Validation to prevent switching to already connected server
    - Loading states and error handling for server changes

### Build and Distribution

- **NuGet** - Package distribution platform
- Package name: `XrplCSharp`
- Build system: `dotnet build` and `dotnet test`