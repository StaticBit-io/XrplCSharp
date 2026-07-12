# Integration Tests

Integration tests (`TestI*` filter) require a local `rippled` node in standalone mode with amendments enabled and an automatic ledger acceptor.

> **Do not** run a bare `rippled -a` or a random standalone image: a node without amendment configuration answers `temDISABLED` to AMM/NFT/MPT/Batch and most other modern transactions. Use the Docker Compose environments below — they enable the required amendments at genesis.

## Run

```bash
# release-node environment (rippled 3.2.0, same as CI)
docker compose -f .ci-config/docker-compose.ci.yml up -d
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestI"
docker compose -f .ci-config/docker-compose.ci.yml down
```

Amendment-gated classes (`TestIBatch`, `TestIDelegateSet`) are skipped on the release node and need the nightly-develop environment instead:

```bash
docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestIBatch|TestIDelegateSet"
docker compose -f .ci-config/docker-compose.batchv11.yml down
```

Tests connect to `ws://localhost:6006` (see `ServerUrl.cs`) and fund wallets from the standalone genesis account (see `Utils.cs`).

Full details — amendment activation, `temDISABLED` troubleshooting, genesis funding, ledger advancement: [Standalone Node Guide](../../../DocFx/StandaloneNode-Guide.md) ([русская версия](../../../DocFx/StandaloneNode-Guide.ru.md)).
