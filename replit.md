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