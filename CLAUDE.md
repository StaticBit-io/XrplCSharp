# XrplCSharp — C# SDK for XRP Ledger

A pure C# implementation for interacting with the XRP Ledger. Published as the `Xrpl` NuGet package.
Provides native C# methods and models for XRPL transactions, serialization, transaction signing, and the rippled WebSocket API.

Repository: [github.com/StaticBit-io/XrplCSharp](https://github.com/StaticBit-io/XrplCSharp)

## Technology Stack

- **.NET 8 / 9 / 10** — multi-target (`net8.0;net9.0;net10.0` for main library)
- **C# latest** — `<LangVersion>latest</LangVersion>` in main Xrpl project
- Base libraries target **netstandard2.0** (AddressCodec, BinaryCodec) and **netstandard2.1** (Keypairs)
- **System.Text.Json** — JSON serialization in client code
- **NBitcoin** 8.0.13 — Bitcoin/crypto primitives
- **Portable.BouncyCastle** 1.9.0 — cryptographic operations
- **Chaos.NaCl.Standard** 1.0.0 — Ed25519 operations
- **MSTest** — test framework
- **Flurl.Http** — HTTP client in tests

## Project Structure

Solution file: `XrplCSharp.sln`

```
XrplCSharp/
├── Base/                              # Low-level codec libraries
│   ├── Xrpl.AddressCodec/            # Base58, XRP address encoding/decoding (netstandard2.0)
│   ├── Xrpl.BinaryCodec/            # XRPL binary serialization format (netstandard2.0)
│   └── Xrpl.Keypairs/               # Ed25519/secp256k1 key generation and signing (netstandard2.1)
│
├── Xrpl/                             # Main SDK library (net8.0;net9.0;net10.0)
│   ├── Client/                       # XrplClient — WebSocket client to rippled
│   │   ├── IXrplClient.cs           # Main interface for consumers
│   │   ├── XrplClient.cs            # WebSocket connection, RPC methods, subscriptions
│   │   └── Json/                    # JSON converters for XRPL types
│   ├── Models/                       # Request/response DTOs for rippled API
│   │   ├── Methods/                 # account_info, account_lines, tx, submit, etc.
│   │   ├── Transactions/           # Payment, TrustSet, OfferCreate, AMMCreate, etc.
│   │   ├── Subscriptions/          # Stream events (ledger, transactions, accounts)
│   │   ├── Ledger/                 # Ledger objects (AccountRoot, RippleState, Offer, etc.)
│   │   ├── Common/                 # Currency, Amount, shared types
│   │   └── Utils/                  # Utility models
│   ├── Sugar/                       # High-level helpers (auto-fill, submit-and-wait, fee estimation)
│   ├── Wallet/                      # XrplWallet — key generation, signing, testnet funding
│   ├── Utils/                       # Hashing, utilities
│   └── Properties/
│
├── Tests/
│   ├── Xrpl.Tests/                  # Unit (TestU) + Integration (TestI) tests for main library
│   ├── Xrpl.AddressCodec.Test/     # AddressCodec unit tests
│   ├── Xrpl.BinaryCodec.Test/     # BinaryCodec unit tests
│   ├── Xrpl.Keypairs.Test/        # Keypairs unit tests
│   └── TestsClients/
│       ├── Test.ClonsoleApp/       # Console demo app
│       └── Blazor-WebAssembly/     # Blazor WASM demo app
│
├── Tools/
│   └── GenerateEnums/              # Code generator for XRPL enums from rippled definitions (net8.0)
│
├── DocFx/                           # Documentation generator config
├── docs/                            # Generated HTML documentation
│
├── .ci-config/
│   ├── docker-compose.ci.yml       # Docker Compose: rippled standalone + ledger-acceptor
│   ├── rippled.cfg                 # rippled configuration for CI
│   └── validators.txt              # Validator config for CI
│
├── XrplCSharp.sln                   # Solution file
├── test.runsettings                 # MSTest config: parallel at class level
├── azure-pipelines.yml              # Legacy CI (outdated, references old project names)
├── CONTRIBUTING.md                  # Development setup and release process
├── CHANGES.md                       # Changelog
└── README.md                        # Usage examples and documentation links
```

## Architecture

### Package Hierarchy

```
Xrpl (main NuGet package)
├── Xrpl.BinaryCodec (binary serialization)
│   └── Xrpl.AddressCodec (Base58, address codec)
└── Xrpl.Keypairs (Ed25519/secp256k1)
    └── Xrpl.AddressCodec
```

### Key Public API

- **`XrplClient`** (`Xrpl.Client`) — main entry point; WebSocket connection to rippled, RPC methods (AccountInfo, Fee, ServerInfo, Subscribe, etc.), event-driven API (`OnConnected`, etc.)
- **`IXrplClient`** — interface for DI and testing
- **`XrplWallet`** (`Xrpl.Wallet`) — wallet generation, seed import, transaction signing
- **`WalletSugar`** — testnet faucet funding
- **`Xrpl.Sugar`** — auto-fill transaction fields, `SubmitAndWait`, fee helpers
- **`Xrpl.Models.*`** — full set of DTOs for rippled API methods, transactions, ledger objects

### Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `Xrpl.Client` | WebSocket client and JSON-RPC interface |
| `Xrpl.Client.Json` | JSON converters for XRPL types |
| `Xrpl.Models.Methods` | Request/response types for rippled API commands |
| `Xrpl.Models.Transactions` | Transaction type models (Payment, TrustSet, etc.) |
| `Xrpl.Models.Subscriptions` | Subscription/stream event models |
| `Xrpl.Models.Ledger` | Ledger entry types (AccountRoot, RippleState, etc.) |
| `Xrpl.Models.Common` | Currency, Amount, and shared types |
| `Xrpl.Sugar` | High-level transaction helpers |
| `Xrpl.Wallet` | Key management and signing |
| `Xrpl.Utils` | Hashing and utility functions |
| `Xrpl.BinaryCodec` | XRPL binary serialization |
| `Xrpl.BinaryCodec.Enums` | Generated enums from rippled definitions |
| `Xrpl.BinaryCodec.Types` | Serialization field types |
| `Xrpl.AddressCodec` | Base58 and address encoding |
| `Xrpl.Keypairs` | Ed25519 and secp256k1 key operations |

## Build & Test

### Prerequisites

- .NET 10 SDK (for development)
- Docker + Docker Compose (for integration tests)

### Build

```bash
dotnet restore
dotnet build
```

### Unit Tests

```bash
dotnet test --verbosity normal --settings test.runsettings --filter "TestU"
```

### Integration Tests

Integration tests require a standalone `rippled` node. The Docker Compose setup provides one with automatic ledger acceptance:

```bash
# Start rippled in standalone mode
docker compose -f .ci-config/docker-compose.ci.yml up -d

# Run integration tests
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --verbosity normal --settings test.runsettings --filter "TestI"

# Stop containers
docker compose -f .ci-config/docker-compose.ci.yml down
```

The Docker setup exposes:
- Port 5005 — JSON-RPC
- Port 5006 — WebSocket
- Port 6006 — Admin WebSocket

The `ledger-acceptor` container calls `ledger_accept` every 4 seconds to advance the standalone ledger.

### Generate Documentation

```bash
dotnet tool install -g docfx
docfx DocFx/docfx.json
```

Output goes to `docs/` directory. Published to GitHub Pages.

## CI/CD

### GitHub Actions (`.github/workflows/`)

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `dotnet.test.yml` | Push/PR to `dev`, `main` | Build + unit tests (`TestU`) + integration tests (`TestI` with Docker rippled) |
| `nuget.release.yml` | Push to `release` | Build Release → Pack → Publish to GitHub Packages + NuGet.org |
| `docs.yml` | Push to `main` | DocFx → GitHub Pages |

### Release Process

1. Ensure all tests pass on `dev`
2. Update version in all `.csproj` files (`Xrpl`, `Xrpl.AddressCodec`, `Xrpl.BinaryCodec`, `Xrpl.Keypairs`)
3. Update `CHANGES.md`
4. Merge `dev` → `main` → `release`
5. NuGet publish triggers automatically on push to `release`
6. Create GitHub release with tag

### NuGet Packages Published

- `Xrpl` (main package)
- `Xrpl.AddressCodec`
- `Xrpl.BinaryCodec`
- `Xrpl.Keypairs`

## Code Generation

The `Tools/GenerateEnums/` project generates C# enums from rippled's binary codec definitions.
Source definitions are used to keep `Xrpl.BinaryCodec.Enums` in sync with the latest rippled protocol changes.
Generated files have `*.Generated.cs` suffix.

Reference: [xrpl-codec-gen](https://github.com/RichardAH/xrpl-codec-gen)

## Development Notes

- No `Directory.Build.props` or `global.json` — versions are managed per `.csproj`
- No centralized package management — each project specifies its own NuGet versions
- `azure-pipelines.yml` is **outdated** (references old `RippleDotNet` project name) — use GitHub Actions workflows instead
- `test.runsettings` configures MSTest parallel execution at class level
- `.editorconfig` is minimal (primarily CS8632 suppression)
- The library uses `System.Text.Json` (not Newtonsoft.Json) for serialization
