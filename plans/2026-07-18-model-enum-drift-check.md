# Models-layer enum drift check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A unit test that fails when `Xrpl.Models.TransactionType` / `LedgerEntryType` member sets drift from the protocol `definitions.json` (accounting for the documented `Invalid` / `Unknown` exceptions), so a new type added in only one place is caught in CI.

**Architecture:** Test-only. Link `Base/Xrpl.BinaryCodec/Enums/definitions.json` into `Tests/Xrpl.Tests` as copy-to-output; a `TestU`-prefixed test class reflects each Models enum's member names, parses the matching `definitions.json` section keys inline (System.Text.Json), and asserts bidirectional parity minus a per-enum exception allow-list. No generation, no changes to the `Xrpl` package.

**Tech Stack:** C# / .NET 10 (`Tests/Xrpl.Tests`), MSTest, `System.Text.Json`, reflection (`Enum.GetNames`). Runs in the existing CI unit job (`dotnet test --filter "TestU"`).

**Reference spec:** `specs/2026-07-18-model-enum-drift-check-design.md`

---

## File Structure

```text
Tests/Xrpl.Tests/Xrpl.Tests.csproj          — MODIFY: link definitions.json as copy-to-output
Tests/Xrpl.Tests/Models/TestUModelEnumDrift.cs — CREATE: the drift test (both enums + shared helper)
```

The test class lives in namespace `XrplTests.Xrpl.Models` (matching the sibling tests). The Models enums are `Xrpl.Models.TransactionType` and `Xrpl.Models.LedgerEntryType`. `Tests/Xrpl.Tests` already references `..\..\Xrpl\Xrpl.csproj`, so those types are visible.

---

### Task 1: Link definitions.json and write the drift test

**Files:**
- Modify: `Tests/Xrpl.Tests/Xrpl.Tests.csproj`
- Create: `Tests/Xrpl.Tests/Models/TestUModelEnumDrift.cs`

- [ ] **Step 1: Link definitions.json into the test project as copy-to-output**

In `Tests/Xrpl.Tests/Xrpl.Tests.csproj`, add a new `<ItemGroup>` (place it next to the existing `<ItemGroup>` blocks, e.g. right before the `<ProjectReference>` group):

```xml
  <ItemGroup>
    <None Include="..\..\Base\Xrpl.BinaryCodec\Enums\definitions.json" Link="definitions.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

This copies `definitions.json` next to the test assembly so the test reads it from `AppContext.BaseDirectory` regardless of working directory.

- [ ] **Step 2: Write the drift test**

Create `Tests/Xrpl.Tests/Models/TestUModelEnumDrift.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Drift guard for the hand-written Models enums against the protocol
    /// definitions (#42 part a). A transaction/ledger-entry type added to
    /// definitions.json but forgotten in the Models enum (or vice versa) fails
    /// here. Intentional divergences are recorded in the per-enum allow-lists:
    /// definitions.json carries "Invalid" (the Models enums omit it), and the
    /// Models enums add a synthetic "Unknown" sentinel the converters fall back
    /// to for unrecognized values.
    /// </summary>
    [TestClass]
    public class TestUModelEnumDrift
    {
        // definitions.json has these; the Models enums intentionally omit them.
        private static readonly HashSet<string> DefinitionsOnly =
            new(StringComparer.Ordinal) { "Invalid" };

        // The Models enums add these synthetic members (not protocol types).
        private static readonly HashSet<string> ModelsOnly =
            new(StringComparer.Ordinal) { "Unknown" };

        [TestMethod]
        public void TestUTransactionTypeEnum_MatchesDefinitions()
        {
            AssertEnumMatchesDefinitions(typeof(global::Xrpl.Models.TransactionType), "TRANSACTION_TYPES");
        }

        [TestMethod]
        public void TestULedgerEntryTypeEnum_MatchesDefinitions()
        {
            AssertEnumMatchesDefinitions(typeof(global::Xrpl.Models.LedgerEntryType), "LEDGER_ENTRY_TYPES");
        }

        private static void AssertEnumMatchesDefinitions(Type enumType, string section)
        {
            HashSet<string> modelNames = new(Enum.GetNames(enumType), StringComparer.Ordinal);
            HashSet<string> definitionKeys = LoadSectionKeys(section);

            List<string> missingInModels = definitionKeys
                .Where(k => !modelNames.Contains(k) && !DefinitionsOnly.Contains(k))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            List<string> extraInModels = modelNames
                .Where(n => !definitionKeys.Contains(n) && !ModelsOnly.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (missingInModels.Count == 0 && extraInModels.Count == 0)
                return;

            string message =
                $"{enumType.Name} drift vs definitions.json ({section}):" + Environment.NewLine +
                $"  missing in Models (add them):      {Format(missingInModels)}" + Environment.NewLine +
                $"  extra in Models (remove/justify):  {Format(extraInModels)}" + Environment.NewLine +
                "If a divergence is intentional, add the name to the DefinitionsOnly/ModelsOnly allow-list with a reason.";
            Assert.Fail(message);
        }

        private static string Format(List<string> names) =>
            names.Count == 0 ? "(none)" : string.Join(", ", names);

        private static HashSet<string> LoadSectionKeys(string section)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "definitions.json");
            Assert.IsTrue(File.Exists(path),
                $"definitions.json was not copied next to the test assembly (expected at {path}).");

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.IsTrue(doc.RootElement.TryGetProperty(section, out JsonElement sectionElement),
                $"definitions.json is missing the '{section}' section.");

            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (JsonProperty prop in sectionElement.EnumerateObject())
                keys.Add(prop.Name);
            return keys;
        }
    }
}
```

- [ ] **Step 3: Build and run the two tests — expect PASS**

The Models enums are in sync with the current `definitions.json` (modulo the allow-listed `Invalid`/`Unknown`), so both tests must pass immediately.

Run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --filter "TestUTransactionTypeEnum_MatchesDefinitions|TestULedgerEntryTypeEnum_MatchesDefinitions" --settings test.runsettings -v minimal
```
Expected: PASS (2 tests). If either FAILS, read the printed missing/extra list: either the Models enum genuinely drifted (a real finding — report it, do not paper over it) or an intentional divergence is not yet allow-listed. Do NOT weaken the assertion to force a pass.

- [ ] **Step 4: Commit**

```bash
git add Tests/Xrpl.Tests/Xrpl.Tests.csproj Tests/Xrpl.Tests/Models/TestUModelEnumDrift.cs
git commit -m "test(models): drift guard for TransactionType/LedgerEntryType vs definitions.json (#42)"
```
Do NOT add any AI attribution / co-author trailer to the commit message.

---

### Task 2: Prove the guard catches drift, then run the full unit suite

This task adds no committed code — it verifies the guard actually fails when it should, then confirms the whole unit suite is green.

- [ ] **Step 1: Negative check — an un-allow-listed divergence must fail**

Temporarily empty the `ModelsOnly` allow-list so the real `Unknown` member (which is genuinely absent from `definitions.json`) surfaces as an `extra in Models` divergence. Make this one-line edit in `Tests/Xrpl.Tests/Models/TestUModelEnumDrift.cs`:

```csharp
private static readonly HashSet<string> ModelsOnly =
    new(StringComparer.Ordinal) { }; // TEMP: was { "Unknown" }
```

Then run:
```bash
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --filter "TestUTransactionTypeEnum_MatchesDefinitions" --settings test.runsettings -v minimal
```
Expected: FAIL, with a message containing `extra in Models (remove/justify):  Unknown`. This proves the guard detects a member present in Models but absent from `definitions.json`.

- [ ] **Step 2: Revert the perturbation**

Restore `ModelsOnly` to `new(StringComparer.Ordinal) { "Unknown" }`. Confirm the file matches the committed version:
```bash
git diff Tests/Xrpl.Tests/Models/TestUModelEnumDrift.cs
```
Expected: no diff (the perturbation is fully reverted).

- [ ] **Step 3: Run the full unit suite the CI way**

Run:
```bash
dotnet test --settings test.runsettings --filter "TestU"
```
Expected: all projects pass, including the two new `TestUModelEnumDrift` tests in `Xrpl.Tests`.

- [ ] **Step 4: No commit**

Nothing to commit (the negative check was reverted). Report that the guard passes positively and fails on injected drift, and that the full suite is green.

---

## Notes for the implementer

- **Test-only, no package change.** Do not modify the `Xrpl` package, the Models enums, their doc comments, the `Unknown` sentinel, or any converter. Do not bump any version.
- **Do not weaken the assertion to force a green.** If the positive run (Task 1 Step 3) fails, it is either a real drift (report it) or a missing allow-list entry (add it with a comment). Never delete the comparison or make it trivially pass.
- **`global::Xrpl.Models.TransactionType`** — the `global::` prefix avoids any ambiguity with the test's `XrplTests.Xrpl.Models` namespace, which also contains `Xrpl` as a segment.
- **Filter names are FullyQualifiedName substrings.** The two test methods carry the `TestU` prefix so the CI unit job (`--filter "TestU"`) runs them.
