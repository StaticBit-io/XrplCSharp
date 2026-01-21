# XrplCSharp

## Overview
XrplCSharp is a pure C# implementation for interacting with the XRP Ledger, designed to simplify complex operations like serialization, transaction signing, wallet management, and network communication. It provides a comprehensive SDK for building .NET applications on the XRP Ledger, built on .NET 10.0 with a modular architecture. The project aims to offer robust, real-time capabilities for integrating with the XRP Ledger.

## User Preferences
Preferred communication style: Simple, everyday language.

## System Architecture
XrplCSharp utilizes a monorepo structure with distinct packages for core functionalities: `Xrpl.AddressCodec` for address encoding, `Xrpl.BinaryCodec` for binary serialization, `Xrpl.Keypairs` for key management, and `Xrpl` as the main client library.

### Network Client
The `XrplClient` facilitates robust, real-time WebSocket communication with `rippled` nodes. Key architectural features include:
- **Connection Management**: Advanced event handling for connection status, intelligent reconnection logic with configurable policies, and detailed diagnostics, including fast server switching and immediate network drop recovery. It includes specific handling for MAUI/iOS network errors to prevent critical logging.
- **Request Handling**: Asynchronous request/response patterns with configurable timeouts and failure policies for resilience.
- **Protocol Features**: Rate-limit detection and an application-level ping/pong heartbeat.
- **Error Handling**: Comprehensive exception containment and prevention of `UnobservedTaskException` and silent promise rejection.
- **Reconnect Logic**: A unified reconnect flow using session isolation and a `ReconnectMode` enum for consistent state management and telemetry.

### Wallet Management
The `XrplWallet` class provides secure generation and management of XRP Ledger wallets, supporting random and deterministic creation, ED25519 and SECP256K1 key derivation, and custom Base58 encoding/decoding.

### Binary Codec System
A comprehensive binary codec handles the XRP Ledger's canonical binary format, supporting bidirectional conversion between binary and JSON for various data types.

### Cryptographic Operations
The library supports ED25519 and SECP256K1 signature algorithms, integrating with external libraries like `Chaos.NaCl.Standard` for ED25519.

### Address Encoding Scheme
A custom Base58 codec (`B58`) handles XRP Ledger's unique address and seed encoding, including version prefixes, checksum validation, and support for classic and X-addresses.

### Feature Specifications
- **Oracle Support (XLS-47 Price Feeds)**: Implementation for `OracleSet` and `OracleDelete` transactions, `LOOracle` ledger entries, and `PriceData` models. Includes specific `BinaryCodec` updates for Oracle-related fields and comprehensive test suites. This also includes critical fixes for `LastUpdateTime` epoch, hex case normalization, currency binary encoding, and the missing `Scale` field.
- **Clawback Transaction Support**: Full implementation of the `Clawback` transaction type, allowing token issuers to recover tokens. This requires the `asfAllowTrustLineClawback` flag to be set on the issuer account.
- **AMMClawback Transaction Support**: Full implementation of the `AMMClawback` transaction type, enabling token issuers to recover tokens deposited into Automated Market Maker (AMM) pools. This also requires the `asfAllowTrustLineClawback` flag.

### Documentation
API documentation is generated from XML comments using DocFX.

## External Dependencies

### NuGet Packages
- **Chaos.NaCl.Standard**: Used for ED25519 cryptographic operations.
- **Microsoft.CSharp**: Utilized for dynamic language features.
- **Microsoft.Extensions.Logging.Abstractions**: Provides logging abstraction.

### XRP Ledger Infrastructure
- **rippled nodes**: WebSocket connections (wss://) to XRP Ledger nodes, including Testnet and production environments.
- **Docker container**: `xrpllabsofficial/xrpld:1.12.0` used for integration testing purposes.