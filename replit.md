# XrplCSharp

## Overview

XrplCSharp is a pure C# implementation for interacting with the XRP Ledger. Its primary purpose is to simplify complex XRP Ledger operations, including serialization, transaction signing, wallet management, and network communication. It provides a comprehensive SDK for building applications on the XRP Ledger using C# and .NET technologies. The library is built on .NET 10.0 (SDK 10.0.101) and features a modular architecture, enabling developers to integrate specific functionalities as needed.

## User Preferences

Preferred communication style: Simple, everyday language.

## System Architecture

### Modular Package Design

XrplCSharp adopts a monorepo structure with distinct, focused packages:
- `Xrpl.AddressCodec`: Handles address and seed encoding/decoding.
- `Xrpl.BinaryCodec`: Manages binary serialization for the XRP Ledger format.
- `Xrpl.Keypairs`: Provides cryptographic key generation and management.
- `Xrpl`: The main client library, integrating all other components for a full SDK experience.

This modularity allows for separation of concerns, reusability, and maintainability, letting developers use only the necessary components.

### WebSocket-Based Network Client

The core of the network interaction is a robust WebSocket client (`XrplClient`) designed for persistent, real-time communication with `rippled` nodes. It leverages C#'s async/await pattern for idiomatic usage and provides:
- **Connection Management**: Event handling for connection status (`OnConnected`, `OnDisconnect`), intelligent reconnection logic with configurable policies, and detailed connection closure diagnostics.
- **Request Handling**: Asynchronous request/response patterns with support for configurable timeouts and request failure policies (`ImmediateFail` or `WaitForConnection`) to ensure resilience during network disruptions. The ping/pong heartbeat mechanism uses `ImmediateFail` policy to rapidly detect connection issues without blocking reconnection attempts, preventing queue buildup of waiting requests during outages.
- **Protocol Features**: Rate-limit detection and handling for XRPL server responses, and an application-level ping/pong heartbeat mechanism to maintain connection integrity and prevent server-side timeouts.
- **State Management**: Accurate reporting of WebSocket states and enhanced reconnection progress tracking to provide detailed status updates to consuming applications.
- **Auto-Reconnect Behavior**: The client intelligently distinguishes between user-initiated and server-initiated disconnections. When a user explicitly calls `Disconnect()`, the connection is closed permanently. However, when the server closes the connection (including WebSocket close code 1000 for normal closures during server restarts or maintenance), the client automatically attempts to reconnect using exponential backoff. This ensures 24/7 bot operations remain resilient to server-side disruptions while respecting explicit user disconnect commands. Implementation uses thread-safe instance tracking via `Interlocked` operations to reliably identify disconnect intent even in asynchronous contexts.

### Wallet Management

The `XrplWallet` class provides functionalities for securely generating and managing XRP Ledger wallets. It supports:
- Random wallet generation and deterministic wallet creation from seeds.
- Key derivation for both ED25519 and SECP256K1 algorithms.
- Address encoding/decoding using a custom Base58 implementation.

### Binary Codec System

The XRP Ledger utilizes a canonical binary format for transactions. XrplCSharp includes a comprehensive binary codec with a type system to handle this:
- **Bidirectional Conversion**: Supports parsing and serialization between binary and JSON representations.
- **Type-Specific Handling**: Includes specialized handlers for various XRP Ledger data types (e.g., Amount, Hash256, AccountID).
- **Extensibility**: Uses serialization/deserialization delegates and a type registry pattern for future expansion.

### Cryptographic Operations

The library supports the multiple signature algorithms required by the XRP Ledger:
- **Algorithm Abstraction**: Provides implementations for ED25519 and SECP256K1 signature schemes.
- **Third-Party Integration**: Delegates ED25519 operations to established cryptographic libraries like Chaos.NaCl.Standard.

### Address Encoding Scheme

A custom Base58 codec (`B58`) is implemented for XRP Ledger's unique address and seed encoding, featuring:
- Version prefixes for different identifier types.
- Checksum validation for data integrity.
- Support for both classic and X-addresses.

### Documentation System

API documentation is generated from XML comments within the source code using DocFX, producing static HTML documentation with cross-references for developers.

## External Dependencies

### NuGet Packages

- **Chaos.NaCl.Standard**: ED25519 cryptographic operations.
- **Microsoft.CSharp**: Dynamic language features.
- **Microsoft.Extensions.Logging.Abstractions**: Logging abstraction layer.

### XRP Ledger Infrastructure

- **rippled nodes**: WebSocket (wss://) connections to XRP Ledger nodes, supporting both Testnet (`wss://s.altnet.rippletest.net:51233`) and standard production endpoints.
- **Docker container**: `xrpllabsofficial/xrpld:1.12.0` for integration testing in standalone mode.

### Testing Infrastructure

- **Unit Tests**: Filter `TestU`.
- **Integration Tests**: Filter `TestI`, requiring a local or Docker-based `rippled` node.
- **Blazor WebAssembly Test Application**: Provides an interactive UI for testing XRP Ledger connectivity and features, including server selection, real-time connection status, and account transaction loading.