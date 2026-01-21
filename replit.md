# XrplCSharp

## Overview

XrplCSharp is a pure C# implementation for interacting with the XRP Ledger. It simplifies complex XRP Ledger operations, including serialization, transaction signing, wallet management, and network communication. The library provides a comprehensive SDK for building applications on the XRP Ledger using C# and .NET. It is built on .NET 10.0 and features a modular architecture.

## User Preferences

Preferred communication style: Simple, everyday language.

## System Architecture

### Modular Package Design

XrplCSharp uses a monorepo structure with distinct, focused packages for address encoding (`Xrpl.AddressCodec`), binary serialization (`Xrpl.BinaryCodec`), key management (`Xrpl.Keypairs`), and the main client library (`Xrpl`). This design promotes reusability and maintainability.

### WebSocket-Based Network Client

The `XrplClient` provides robust, real-time WebSocket communication with `rippled` nodes. Key features include:
- **Connection Management**: Event handling for connection status, intelligent reconnection logic with configurable policies, and detailed closure diagnostics. It distinguishes between user-initiated and server-initiated disconnections for resilient auto-reconnect behavior using exponential backoff.
- **Request Handling**: Asynchronous request/response patterns with configurable timeouts and failure policies to ensure resilience.
- **Protocol Features**: Rate-limit detection and an application-level ping/pong heartbeat to maintain connection integrity.
- **Fast Server Switching**: A per-session architecture allows for quick server changes (3-5 seconds) by retiring old sessions and immediately creating new ones, minimizing downtime and preventing error logging.
- **Immediate Network Drop Reconnect**: Identifies and handles transport-level network failures (e.g., `IOException`, `SocketException`) by attempting immediate reconnection without surfacing critical errors to consuming applications.
- **MAUI/iOS Network Error Recognition**: Extended `IsNetworkException()` and `IsNetworkDropException()` with comprehensive exception chain traversal, message pattern matching (DNS failures like "nodename nor servname"), and platform-specific HRESULTs (0xFFFDFFFF for iOS DNS, 0x80004005 for E_FAIL) to prevent Critical logging on MAUI mobile platforms.
- **Fast Ping Timeout Recovery**: Utilizes the same fast-reconnect mechanism as server switching to quickly recover from ping timeouts without error floods.
- **Deterministic Cleanup**: Ensures complete WebSocket cleanup during disconnect operations, preventing race conditions and resource leaks.
- **Intentional Disconnect Detection**: A multi-layered approach prevents critical error logging during user-initiated disconnections or server changes, differentiating them from genuine network failures.
- **Complete Exception Containment**: All Timer.Elapsed handlers, async callbacks (OnConnect, OnConnectionError, OnMessageReceived, OnDisconnect), and fire-and-forget tasks are wrapped in try-catch blocks to prevent exceptions from escaping to consuming applications.
- **UnobservedTaskException Prevention**: RequestManager.Reject() now automatically "observes" faulted Tasks via ContinueWith, preventing TaskScheduler.UnobservedTaskException from being raised in consuming apps with global exception handlers.
- **Silent Promise Rejection**: Reject() no longer throws if a promise has already been resolved/rejected, enabling safe cleanup during reconnect flows.
- **Unified Reconnect Flow**: ReconnectLoopAsync now uses the same session isolation pattern as ChangeServer - marking old sessions as retiring and creating fresh sessions before each connection attempt, preventing late callbacks from old sockets.
- **Consistent ReconnectInfo Telemetry**: All ConnectionStatusInfo events with RestoringConnection state now include ReconnectInfo (CurrentAttempt, MaxAttempts, RemainingDelay), enabling consuming applications to properly track reconnect progress.
- **ReconnectMode Enum State Machine**: A `ReconnectMode` enum (None, FastReconnect, LoopReconnect) serves as the single source of truth for reconnect state, eliminating race conditions from multi-flag checks. The mode is set atomically before any reconnect begins and cleared only when connection is stable or user disconnects.

### Wallet Management

The `XrplWallet` class handles secure generation and management of XRP Ledger wallets, supporting random and deterministic wallet creation, key derivation (ED25519 and SECP256K1), and custom Base58 address encoding/decoding.

### Binary Codec System

A comprehensive binary codec handles the XRP Ledger's canonical binary format, supporting bidirectional conversion between binary and JSON representations, with type-specific handling for various XRP Ledger data types.

### Cryptographic Operations

The library supports ED25519 and SECP256K1 signature algorithms, integrating with third-party libraries like `Chaos.NaCl.Standard` for ED25519.

### Address Encoding Scheme

A custom Base58 codec (`B58`) handles XRP Ledger's unique address and seed encoding, including version prefixes, checksum validation, and support for classic and X-addresses.

### Documentation System

API documentation is generated from XML comments using DocFX.

## External Dependencies

### NuGet Packages

- **Chaos.NaCl.Standard**: For ED25519 cryptographic operations.
- **Microsoft.CSharp**: For dynamic language features.
- **Microsoft.Extensions.Logging.Abstractions**: For logging abstraction.

### XRP Ledger Infrastructure

- **rippled nodes**: WebSocket (wss://) connections to XRP Ledger nodes, including Testnet and production endpoints.
- **Docker container**: `xrpllabsofficial/xrpld:1.12.0` for integration testing.

## Recent Changes

### Oracle Support (XLS-47 Price Feeds)

Added complete support for Oracle price feeds (XLS-47 specification):
- **OracleSet Transaction**: Creates or updates price oracles on the ledger. Supports up to 10 asset pairs per oracle with PriceData objects containing BaseAsset, QuoteAsset, AssetPrice, and Scale fields.
- **OracleDelete Transaction**: Removes existing oracles from the ledger. Only the oracle owner can delete.
- **LOOracle Ledger Entry**: Represents oracle objects stored on the XRP Ledger with Owner, Provider, AssetClass, PriceDataSeries, LastUpdateTime, and URI fields.
- **PriceData Model**: Wrapper and inner class for price information in oracle transactions and ledger entries.
- **BinaryCodec Updates**: Added Oracle (128) to LedgerEntryType, OracleSet (51) and OracleDelete (52) to TransactionType enums.
- **Ledger Converter Updates**: LOConverter now deserializes Oracle ledger entries.

All new classes include complete English XML documentation summaries.

### BinaryCodec Field Updates for Oracle Support

Added new fields to support Oracle price data serialization:
- **Currency FieldType**: New field type (ordinal 26) for Oracle asset identifiers.
- **CurrencyField**: New field class for BaseAsset and QuoteAsset.
- **OracleDocumentID** (UInt32, nth=51): Unique identifier for the price oracle.
- **LastUpdateTime** (UInt32, nth=15): Unix timestamp of the last oracle update.
- **AssetPrice** (UInt64, nth=23): Scaled asset price value.
- **AssetClass** (Blob, nth=28): Type of asset (currency, commodity, index).
- **Provider** (Blob, nth=29): Oracle provider identifier.
- **BaseAsset** (Currency, nth=1): Primary asset in a trading pair.
- **QuoteAsset** (Currency, nth=2): Quote asset in a trading pair.

### Oracle Test Suites

Comprehensive test coverage for Oracle transactions:
- **TestOracleSet.cs** (10 unit tests): Validates OracleSet transactions including required fields, PriceDataSeries limits (1-10), Scale range (0-10), and missing field detection.
- **TestOracleDelete.cs** (4 unit tests): Validates OracleDelete transactions including required OracleDocumentID field.
- **TestIOracle.cs** (Integration tests): End-to-end tests for Oracle lifecycle on testnet/standalone including create, update, delete, and ledger state verification via AccountObjects queries.
- **Validation.cs Updates**: OracleSet and OracleDelete transaction types are routed to their respective validators.
- **TxFormat.cs Updates**: OracleSet and OracleDelete formats added to the transaction format dictionary for binary codec validation.
- **StObject.cs Currency Registration**: Added FieldType.Currency to the type registration dictionary enabling serialization of BaseAsset and QuoteAsset fields in PriceData objects.
- **Blob.cs ASCII String Support**: Enhanced Blob.FromJson to auto-detect hex vs ASCII strings, enabling Provider and AssetClass fields to accept plain text values.
- **Oracle ID Computation Fix**: Corrected ComputeOracleId to use 2-byte space key (0x0052) instead of 4-byte prefix, matching XRPL ledger entry ID specification.
- **Currency.cs Oracle Encoding**: Added context-aware encoding with separate methods:
  - `FromString` / `FromJson`: Standard XRPL encoding (bytes 12-14) for IOU currencies
  - `FromOracleString` / `FromOracleJson`: XLS-47 encoding (left-aligned bytes 0-2) for Oracle fields
  - `GetCurrencyCodeFromTlcBytes`: Decodes both formats for round-trip support
  - StObject registers `FieldType.Currency` (ordinal 26) with `FromOracleJson` - only affects Oracle BaseAsset/QuoteAsset
  - IOU currencies use `FieldType.Hash160` (ordinal 17) which remains unchanged

### Oracle JSON Serialization Converters (January 2026)

Added custom JSON converters for Oracle transaction fields to ensure correct rippled wire format:
- **AssetPriceConverter**: Converts numeric AssetPrice values to/from lowercase hexadecimal strings (e.g., 65000 → "fde8")
- **OracleCurrencyConverter**: Converts currency codes to/from 40-character hex strings:
  - XRP → "0000000000000000000000000000000000000000" (40 zeros)
  - BTC → "4254430000000000000000000000000000000000" (left-aligned ASCII)
- **OracleHexStringConverter**: Converts Provider, AssetClass, URI fields to/from hex ASCII (e.g., "currency" → "63757272656e6379")
- All converters support bidirectional conversion for both serialization and deserialization
- 19 unit tests validate Oracle transaction and converter functionality

### Oracle Critical Fixes (January 2026)

Fixed critical issues preventing Oracle transactions from being accepted by the network:

- **LastUpdateTime Epoch Fix**: Changed from Unix epoch (1970) to Ripple epoch (2000). XRPL requires timestamps as seconds since January 1, 2000. The offset is 946,684,800 seconds. LastUpdateTime must be within 300 seconds of ledger close time.
- **Hex Case Normalization**: All hex strings in Oracle fields (Provider, AssetClass, currencies >3 chars) now use lowercase hex for consistency with rippled expectations.
- **Currency Binary Encoding**: Oracle BaseAsset/QuoteAsset use XLS-47 format (left-aligned bytes 0-2) for 3-letter codes, matching xrpl.js Oracle serializer. Standard IOU currencies use bytes 12-14. Non-standard currencies (40-hex) use direct bytes.
- **EncodeOracleCurrency Method**: New method in Currency.cs specifically for Oracle XLS-47 format encoding at bytes 0-2.
- **Scale Field Fix (Critical)**: Added missing `Scale` field (UInt8, nth=4) to Field.cs. This field was defined in definitions.json but not registered in Field.cs, causing Scale values in Oracle PriceData to be silently dropped during binary serialization. Now Scale values (0-10) are correctly serialized and deserialized in OracleSet transactions.