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

- **AMM Pool Calculations**: Extension methods on `AMMInfo` (`Xrpl/Models/Utils/AmmInfoExtensions.cs`) providing constant-product (50/50) AMM calculations: `PriceForOneAmount2InAmount()`, `PriceForOneAmountInAmount2()`, `InvariantK()`, `MintLpSingleAssetAmount()`, `MintLpSingleAssetAmount2()`, `MintLpDual()`, `MintLpDualProportional()`, `RedeemDual()`, `RedeemSingleToAmount()`, `RedeemSingleToAmount2()`, `BurnLpForExactAmountOut()`. Uses analytical formulas with fee handling (basis points). Static helper `FeeBpsToDecimal()`. Optimized: uses `DecimalMath.Sqrt` instead of `Power(x, 0.5)`, deduplicated single-sided redemption via private helper.
- **AMM Swap Methods**: `SwapAmount1ForAmount2()`, `SwapAmount2ForAmount1()` — constant-product swap with trading fee, returns `AmmSwapResult` (AmountIn, AmountOut, Fee, UpdatedPool). `GetEffectiveAmmPrice()` returns price in order-book-compatible units. `SpotPrice()` returns raw ratio. `ClonePool()` deep-copies pool state.
- **Trade Simulator** (`Xrpl/Models/Utils/TradeSimulator.cs`): `TradeSimulator.SimulateTrade()` — simulates XRPL trade execution interleaving DEX order book and AMM. Supports three modes: only order book (AMM=null), only AMM (empty book), or both with best-price interleaving. Solves quadratic for AMM delta-to-price alignment. Returns `TradeSimulationResult`: TotalReceived, TotalSpent, FromOrderBook, FromAmm, AmmPoolFee, EffectivePrice, SpotPriceBefore/After, PriceImpactPercent, RemainingOffers, UpdatedAmm, Steps (list of `FillStep` with Source/AmountIn/AmountOut/Price). Filters unfunded offers automatically.

### Blazor WebAssembly UI
- **Location**: `Tests/TestsClients/Blazor-WebAssembly/`
- **Tabs**: Connection Test (`/`) and Swap (`/swap`) via `MainLayout.razor`
- **Swap Simulator** (`Pages/Swap.razor`): Interactive swap calculator for XRP token pairs.
  - Input: Amount, Currency Code, Issuer Address; direction toggle (sell/buy XRP)
  - Debounced input (2s) triggers: `book_offers` + `amm_info` fetch → `TradeSimulator.SimulateTrade()` → `Payment.Simulate()`
  - Results: Two-column display — TradeSimulator metrics (effective price, spot prices, price impact, fill steps) and Payment simulate result (engine result, delivered amount, metadata)
  - XRP amounts converted to drops for TradeSimulator and Payment construction
  - Handles AMM not found gracefully (order-book-only mode)
  - **Order Book (Стакан)**: Full order book visualization next to swap form. Two `book_offers` requests (asks: taker_gets=XRP/taker_pays=Token, bids: reversed). Dark-themed panel with red asks on top (sorted ascending, displayed reversed), green bids on bottom (sorted descending), spread row in middle showing absolute and percentage spread. Colored volume bars (opacity gradient) behind prices proportional to cumulative total. Columns: Цена/Сумма/Итого. Refreshes on token config or amount change via unified debounce.

### Shared Utilities
- **HexStringHelper**: A utility for normalizing and handling hex-encoded variable-length fields, ensuring proper format and conversion.
- **DecimalMath** (`Xrpl/Utils/DecimalMath.cs`): Internal high-precision decimal math library. Provides `Sqrt` (Newton's method), `Power`, `Log`, `Exp`, `PowerN`, `Log10`, `Abs`. Used by AMM calculations and token precision.
- **TokenPrecision** (`Xrpl/Utils/TokenPrecision.cs`): Internal utility for XRPL token amount rounding. XRP: 6 decimal places. Non-XRP: up to 15 significant digits. Methods: `RoundTokenAmount()`, `FormatTokenAmount()`, `TruncateValue()`. Enum `PrecisionRoundingMode`.
- **CurrencyExtensions**: `GetValue()` returns human-readable value (XRP in XRP units, tokens raw). `IsXrp()` checks if currency is native XRP. In `Xrpl/Models/Common/Currency.cs`.

## External Dependencies

### NuGet Packages
- **Chaos.NaCl.Standard**: For ED25519 cryptographic operations.
- **Microsoft.CSharp**: For dynamic language features.
- **Microsoft.Extensions.Logging.Abstractions**: For logging abstraction.

### XRP Ledger Infrastructure
- **rippled nodes**: WebSocket connections (wss://) to XRP Ledger nodes (Testnet and production).
- **Docker container**: `xrpllabsofficial/xrpld:1.12.0` for integration testing.