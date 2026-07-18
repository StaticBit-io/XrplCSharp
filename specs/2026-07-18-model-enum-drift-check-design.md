# Models-layer enum drift check (design)

**Issue:** [#42](https://github.com/StaticBit-io/XrplCSharp/issues/42) part (a). Part (b) — the `diff` command — shipped in PR #57.
**Date:** 2026-07-18
**Status:** approved, pending implementation plan

## Motivation

`Xrpl.Models.TransactionType` and `Xrpl.Models.LedgerEntryType` are hand-written C# enums that duplicate the protocol's transaction/ledger-entry type lists (also compiled into the generated `Xrpl.BinaryCodec` enums). Every new type must be added in two places; drift is a matter of time (the XLS-68 wave touched both by hand).

Issue #42 part (a) originally proposed *generating* the Models enums from `definitions.json`. Exploration showed the Models enums carry hand-authored content a pure generator cannot reproduce:

- **83 / 32 per-member XML doc comments** (public API docs) absent from `definitions.json`.
- A synthetic **`Unknown`** sentinel member (not a protocol type) that `TransactionTypeConverter`/`LedgerEntryType` deserialization falls back to for unrecognized values — load-bearing, must survive.
- Deliberate exclusion of **`Invalid`** (a `definitions.json` entry the Models enum omits).

So the chosen solution is a **drift check**, not generation: a unit test that fails when the Models enum member set diverges from `definitions.json` (accounting for the known, documented exceptions). This catches the "added twice / forgot one" drift — the actual goal — while leaving the doc comments and the `Unknown` sentinel untouched. It reuses the same protocol source (`definitions.json`) as part (b): the `diff` command keeps `definitions.json` in sync with a node; this test keeps the Models enums in sync with `definitions.json`.

## Scope

Exactly the two Models enums that mirror `definitions.json` sections:

| Models enum | `definitions.json` section |
|---|---|
| `Xrpl.Models.TransactionType` | `TRANSACTION_TYPES` |
| `Xrpl.Models.LedgerEntryType` | `LEDGER_ENTRY_TYPES` |

Other `Xrpl.Models` enums (`LedgerEntryFilter`, `StreamType`, `XrplGlobalFlags`, per-transaction flag enums, …) are SDK-specific and not mirrors of `definitions.json`; they are out of scope. There is no Models-layer TER/`TransactionResult` enum (result codes live only in the generated `Xrpl.BinaryCodec.EngineResult`).

Non-goals: generating any `.cs`; changing the Models enums, their doc comments, the `Unknown` sentinel, or serialization; touching the `Xrpl` shipping package. This is test-only.

## Placement & components

- **Home:** `Tests/Xrpl.Tests` (already references `Xrpl`, where the Models enums live; runs in the existing unit CI job via the `TestU` filter). New file `Tests/Xrpl.Tests/Models/TestUModelEnumDrift.cs`.
- **`definitions.json` access:** link the file into the test project as copy-to-output and read it from `AppContext.BaseDirectory` — no fragile repo-root walking:

  ```xml
  <None Include="..\..\Base\Xrpl.BinaryCodec\Enums\definitions.json"
        Link="definitions.json" CopyToOutputDirectory="PreserveNewest" />
  ```

- **Parsing:** the two sections are flat `name → int` maps; parse their keys inline with `System.Text.Json` (a few lines). No dependency on the `GenerateEnums` tool project — the comparison is a plain set operation, and a main-package test project referencing the dev tool would be needless coupling.
- **Two test methods**, one per enum: `TestUTransactionTypeEnum_MatchesDefinitions`, `TestULedgerEntryTypeEnum_MatchesDefinitions`.

## Comparison logic

For each enum:

1. `HashSet<string> modelNames = new(Enum.GetNames(typeof(TargetEnum)), StringComparer.Ordinal);`
2. `HashSet<string> definitionKeys =` the section's keys parsed from `definitions.json`.
3. Build the **exact** member set the Models enum must contain, then compare against it (not merely subtract the allow-lists from each raw diff). The exact-set form also catches an intentionally-omitted name being *added* (e.g. `Invalid`) or a sentinel being *removed* (e.g. `Unknown`) — the allow-lists are passed per-enum so a future enum-specific exception can be scoped without affecting the other:

   ```csharp
   // definitions.json has these; the Models enum intentionally omits them
   static readonly HashSet<string> DefinitionsOnly = new(StringComparer.Ordinal) { "Invalid" };
   // the Models enum adds these synthetic members (not protocol types)
   static readonly HashSet<string> ModelsOnly = new(StringComparer.Ordinal) { "Unknown" };

   // expected = protocol types − intentionally-omitted + synthetic sentinels
   var expected = new HashSet<string>(definitionKeys, StringComparer.Ordinal);
   expected.ExceptWith(DefinitionsOnly);
   expected.UnionWith(ModelsOnly);

   missingInModels = expected.Except(modelNames);   // in the contract, absent from the enum
   extraInModels   = modelNames.Except(expected);   // in the enum, not in the contract
   ```

   Both enums have the identical exception pattern today (verified against the current `definitions.json`): definitions-only = `{ "Invalid" }`, Models-only = `{ "Unknown" }`. The same two allow-lists are passed to both `TransactionType` and `LedgerEntryType`.

4. Fail when either set is non-empty, with an explicit message:

   ```text
   TransactionType drift vs definitions.json:
     missing in Models (add them):      Batch, LoanSet
     extra in Models (remove/justify):  FooBar
   If a divergence is intentional, add the name to the DefinitionsOnly/ModelsOnly allow-list with a reason.
   ```

Member names and `definitions.json` keys match byte-for-byte (`Payment`, `Batch`, `AccountSet`, …); comparison is `Ordinal`.

The allow-list is the point: it records exactly the divergences that are intentional, so any *new* divergence fails the build until someone adds the name to the allow-list with a justification — that is the guard against silent drift.

## Verification

The test is the deliverable. It is proven two ways:

- **Positive:** on current `dev`, both tests pass (Models and `definitions.json` are in sync today, modulo the known exceptions).
- **Negative (proves it catches drift):** a one-time manual check — temporarily remove an entry from the allow-list (or inject a synthetic extra key) and confirm the test fails with the expected missing/extra list, then revert. Described as a plan step, not committed.

Runs in the existing unit job (`dotnet test --filter "TestU"`); zero new infrastructure.

## Risk / compatibility

- **Test-only.** No shipping-package source or version change; no NuGet impact.
- The linked `definitions.json` is copy-to-output; it does not alter the `Xrpl` package.
- Failure mode is a failing unit test with an actionable message — exactly the intended drift gate.
