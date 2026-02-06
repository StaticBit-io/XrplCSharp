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
- **Protocol Features**: Rate-limit detection and an application-level ping/pong heartbeat with fast-path message processing.
- **Fast-Path Message Processing**: Under high-volume stream subscriptions (transactions, ledgers), response messages (including ping/pong) are prioritized over stream data to prevent head-of-line blocking. The `IsLikelyResponse()` method uses pure string scanning of the ENTIRE message (no JSON parsing) to detect responses by presence of `"id"` property - responses always have `"id"`, streams never do. CRITICAL: XRPL places large `"result"` objects BEFORE the `"id"` field, so full-message scanning is required (early versions that only scanned first N characters failed for server_info and other large responses). Stream messages use `Channel<T>` for async background processing on desktop/MAUI, or fire-and-forget pattern on WebAssembly. Response handling calls `requestManager.HandleResponse()` immediately.
- **Connection Health Monitoring (`UseCheckHealth`)**: A lightweight background health check that runs every 20 seconds on all platforms. It only inspects local WebSocket state via `IsConnected()` (State == Open) — no network requests are sent. If the WebSocket is not connected (Closed/Aborted/etc.), automatic reconnection is triggered. The 60-second inactivity timeout only applies when `UseCustomPing` is also enabled, since without keepalive pings there is no expectation of regular activity on idle connections. Automatically enabled when `UseCustomPing` is `true`. Can be enabled independently for fast disconnect detection without keepalive overhead.
- **Unified Fire-and-Forget Keepalive Ping (`UseCustomPing`)**: On ALL platforms (Desktop/MAUI/WebAssembly), when `lastActivityTime` shows activity within 30 seconds (data flowing), a fire-and-forget `{"command":"ping"}` is sent directly to the WebSocket without awaiting a response. This keeps the server-side connection alive without blocking the thread waiting for a pong through the stream backlog (200+ tx/sec on mainnet). WebSocket-level `KeepAliveInterval` (RFC 6455 ping/pong frames) is NOT sufficient — XRPL servers (s1/s2.ripple.com) require application-level activity and will close connections after ~60-80 seconds of client silence regardless of transport-level keepalive. When no activity for 30+ seconds, a full request-response ping is sent with 45-second timeout. In WASM: `ReceiveAsync` timeout is 120 seconds (not 30s, which caused WebSocket Aborted state). `System.Threading.Timer` (backed by JS `setTimeout`) fires reliably every 20 seconds. If ping check detects `!IsConnected()` (State=Closed/Aborted), it triggers immediate reconnection.
- **Error Handling**: Comprehensive exception containment and prevention of `UnobservedTaskException` and silent promise rejection. The `CheckIfNotConnected()` method now correctly treats `Connecting` and `RestoringConnection` states as active attempts, preventing race conditions when calling `ChangeServer()` or manual retry after max reconnect attempts.
- **Reconnect Logic**: A unified reconnect flow using session isolation and a `ReconnectMode` enum for consistent state management and telemetry. `ConnectInternalAsync` now accepts `CancellationToken` with pre- and post-WebSocket-creation cancellation checks, ensuring `ChangeServer()` during active reconnect properly aborts stale connection attempts. Triple-guard protection in `OnceClose`, `OnConnectionFailed`, and `StartReconnectLoop` prevents reconnect counter resets during active loops, ensuring proper attempt count monotonicity.

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
- **DID (Decentralized Identifier) Support**: Full implementation of `DIDSet` and `DIDDelete` transaction types, and `LODID` ledger entry type for managing decentralized identifiers on the XRP Ledger. DIDSet supports Data, DIDDocument, and URI fields (at least one required, each max 256 bytes hex-encoded).
- **Secret Numbers Support (XLS-12d)**: Full implementation of the Secret Numbers format for encoding XRPL account secrets as 8 groups of 6 digits. This user-friendly, language-agnostic format includes position-dependent checksums for real-time typo detection. Key methods: `XummExtension.EntropyToSecretNumbers()`, `XummExtension.RandomSecretNumbers()`, `XummExtension.CalculateChecksum()`, `XrplWallet.FromSecretString()`, `XrplWallet.GetSecretNumbers()`, `XrplWallet.GetSecretString()`.
- **BIP-39 Mnemonic Generation**: Full implementation of BIP-39 mnemonic phrase generation with configurable word counts. Supports 12, 15, 18, 21, or 24 words with corresponding entropy levels (128-256 bits). Key method: `XrplWallet.GenerateMnemonic(wordCount)`.
- **Stream Subscriptions**: Real-time subscription to transaction and ledger streams via `client.Subscribe()`. The Blazor test app demonstrates thread-safe event handling with throttled UI updates (500ms intervals), bounded work per tick (max 50 logs), bounded queue (max 500 entries), reentrancy guards using Interlocked operations, automatic cleanup on disconnect, and automatic subscription restoration on reconnect. Statistics include transaction counts by type (Payment, OfferCreate, etc.). **WebAssembly Optimization**: Fire-and-forget keepalive pings maintain server-side connection without blocking the single WASM thread. ReceiveAsync uses 120-second timeout to prevent premature WebSocket abortion. Ping check triggers immediate reconnection when disconnected state is detected. Desktop/MAUI uses `Channel<T>` for true background processing and WebSocket-level keepalive frames.

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