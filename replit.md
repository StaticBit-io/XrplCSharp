# XrplCSharp

## Overview
XrplCSharp is a comprehensive .NET SDK providing a pure C# implementation for interacting with the XRP Ledger. It simplifies complex operations such as serialization, transaction signing, wallet management, and network communication, enabling robust, real-time application development on the XRP Ledger. The project aims to offer a complete toolkit for .NET developers to build and integrate with the XRP Ledger efficiently.

## User Preferences
Preferred communication style: Simple, everyday language.
Preferred language: Russian (русский).

## System Architecture
XrplCSharp is structured as a monorepo, with core functionalities divided into distinct packages: `Xrpl.AddressCodec`, `Xrpl.BinaryCodec`, `Xrpl.Keypairs`, and the main `Xrpl` client library.

### Network Client
The `XrplClient` manages real-time WebSocket communication with `rippled` nodes, featuring:
- **Connection Management**: Advanced event handling, intelligent reconnection logic with configurable policies, fast server switching, and immediate network drop recovery. Includes specific handling for MAUI/iOS network errors.
- **Request Handling**: Asynchronous request/response patterns with configurable timeouts and failure policies.
- **Protocol Features**: Rate-limit detection and application-level ping/pong heartbeat with fast-path message processing.
- **Fast-Path Message Processing**: Prioritizes response messages over stream data using full-message string scanning for `id` presence to prevent head-of-line blocking under high stream volumes.
- **Connection Health Monitoring (`UseCheckHealth`)**: A lightweight background check of local WebSocket state to trigger automatic reconnection if not connected.
- **Unified Fire-and-Forget Keepalive Ping (`UseCustomPing`)**: Sends application-level pings to maintain server-side connections on all platforms, complementing WebSocket-level keepalives.
- **Error Handling**: Comprehensive exception containment and prevention of unhandled task exceptions.
- **Reconnect Logic**: A unified flow using session isolation and a `ReconnectMode` enum for consistent state management and telemetry.

### Wallet Management
The `XrplWallet` class supports secure generation and management of XRP Ledger wallets, including random and deterministic creation, ED25519 and SECP256K1 key derivation, and custom Base58 encoding/decoding. This includes support for Secret Numbers (XLS-12d) and BIP-39 mnemonic generation/validation.

### Binary Codec System
A comprehensive binary codec handles bidirectional conversion between the XRP Ledger's canonical binary format and JSON for various data types.

### Cryptographic Operations
The library supports ED25519 and SECP256K1 signature algorithms.

### Address Encoding Scheme
A custom Base58 codec (`B58`) handles XRP Ledger's unique address and seed encoding, including version prefixes, checksum validation, and support for classic and X-addresses.

### Feature Specifications
- **Oracle Support (XLS-47)**: Implements `OracleSet`, `OracleDelete` transactions, `LOOracle` ledger entries, and `PriceData` models.
- **Clawback & AMMClawback Transactions**: Supports `Clawback` and `AMMClawback` transactions for token recovery by issuers, requiring the `asfAllowTrustLineClawback` flag.
- **DID (Decentralized Identifier) Support**: Implements `DIDSet`, `DIDDelete` transactions, and `LODID` ledger entry type for managing DIDs.
- **TokenEscrow (XLS-85)**: Extends escrow functionality to include fungible tokens (IOUs and MPTs) in `EscrowCreate`, with updated `LOEscrow` ledger entry and `AccountRootFlags`.
- **Stream Subscriptions**: Provides real-time subscriptions to transaction and ledger streams, with optimizations for WebAssembly and robust event handling.
- **PermissionedDomain (XLS-80)**: Implements `PermissionedDomainSet`, `PermissionedDomainDelete` transactions, and `LOPermissionedDomain` ledger entry for credential-based access control.
- **Permissioned DEX (XLS-81)**: Adds `DomainID` to `OfferCreate` and `Payment` transactions, `LOOffer` ledger entries, and `tfHybrid` flag for domain-restricted and hybrid trading.
- **Credentials (XLS-70)**: Implements `CredentialCreate`, `CredentialAccept`, and `CredentialDelete` for on-chain identity verification, with `LOCredential` ledger entry support.
- **MPToken Metadata Schema (XLS-89)**: Implements a standardized metadata schema for Multi-Purpose Tokens, allowing typed access to on-chain metadata fields and serialization/deserialization utilities.

### Shared Utilities
- **HexStringHelper**: A utility for normalizing and handling hex-encoded variable-length fields, ensuring proper format and conversion.

## External Dependencies

### NuGet Packages
- **Chaos.NaCl.Standard**: For ED25519 cryptographic operations.
- **Microsoft.CSharp**: For dynamic language features.
- **Microsoft.Extensions.Logging.Abstractions**: For logging abstraction.

### XRP Ledger Infrastructure
- **rippled nodes**: WebSocket connections (wss://) to XRP Ledger nodes (Testnet and production).
- **Docker container**: `xrpllabsofficial/xrpld:1.12.0` for integration testing.