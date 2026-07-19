# definitions.json vs develop drift monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A weekly CI monitor that runs the `diff` command against a rippled *develop* node (the nightly stand) and fails when the SDK's `definitions.json` drifts from develop, plus a neutral rewording of the tool's misleading `node-only (SDK behind)` label.

**Architecture:** Two independent, small changes. (1) A new scheduled GitHub Actions workflow that builds the existing pinned-develop nightly stand, waits for it, runs `dotnet run -- diff http://localhost:5005`, and lets the tool's exit code gate the job (`1` = node-only/mismatch = SDK behind develop → red; `2` = tool error → red). (2) A one-line label change in `DiffRenderer` from a direction-asserting `(SDK behind)` to a neutral `(on node, not local)`, pinned by a unit test.

**Tech Stack:** GitHub Actions (YAML + bash), Docker Compose (`.ci-config/docker-compose.batchv11.yml` — pinned xrpld develop via `Dockerfile.nightly`), the existing `Tools/GenerateEnums` `diff` command, .NET 10 (`DOTNET_VERSION: '10.0.x'`), MSTest.

**Reference spec:** `specs/2026-07-19-definitions-develop-watch-design.md`

---

## File Structure

```text
Tools/GenerateEnums/Definitions/DiffRenderer.cs                 — MODIFY: neutral node-only label (1 line)
Tests/Xrpl.GenerateEnums.Test/TestUDiffRenderer.cs             — MODIFY: pin the new label
.github/workflows/definitions-watch.yml                        — CREATE: weekly develop-drift monitor
```

The `diff` command, exit-code contract (0/1/2), and the nightly develop stand already exist and are unchanged. This plan only reuses them.

---

### Task 1: Neutral `node-only` label

**Files:**
- Modify: `Tools/GenerateEnums/Definitions/DiffRenderer.cs`
- Modify: `Tests/Xrpl.GenerateEnums.Test/TestUDiffRenderer.cs`

- [ ] **Step 1: Add a pin assertion for the new label (failing test)**

In `Tests/Xrpl.GenerateEnums.Test/TestUDiffRenderer.cs`, inside `TestURenderTable_ShowsCategoriesAndSummary`, add one assertion after the existing `StringAssert.Contains(text, "NewField");` line:

```csharp
        StringAssert.Contains(text, "node-only (on node, not local)");
```

The existing method (for reference — do not duplicate it, just add the one line above into it):

```csharp
    [TestMethod]
    public void TestURenderTable_ShowsCategoriesAndSummary()
    {
        string text = DiffRenderer.RenderTable(Sample());

        StringAssert.Contains(text, "FIELDS");
        StringAssert.Contains(text, "NewField");
        StringAssert.Contains(text, "node-only (on node, not local)");
        StringAssert.Contains(text, "OldField");
        StringAssert.Contains(text, "Sponsor");
        StringAssert.Contains(text, "27 -> 28");
        StringAssert.Contains(text, "Summary");
    }
```

- [ ] **Step 2: Run the test — verify it FAILS**

Run:
```bash
dotnet test Tests/Xrpl.GenerateEnums.Test/Xrpl.GenerateEnums.Test.csproj --filter "TestURenderTable_ShowsCategoriesAndSummary" --settings test.runsettings -v minimal
```
Expected: FAIL — the current label is `node-only (SDK behind)`, so the new substring is absent.

- [ ] **Step 3: Change the label in `DiffRenderer`**

In `Tools/GenerateEnums/Definitions/DiffRenderer.cs`, the `node-only` line inside `RenderTable` currently reads:

```csharp
            foreach (string n in s.NodeOnly) { sb.AppendLine($"  node-only (SDK behind):   + {n}"); nodeOnly++; }
```

Change it to:

```csharp
            foreach (string n in s.NodeOnly) { sb.AppendLine($"  node-only (on node, not local):   + {n}"); nodeOnly++; }
```

Do NOT change the `mismatch` or `local-only (info)` lines or the `Summary` line.

- [ ] **Step 4: Run the test — verify it PASSES**

Run:
```bash
dotnet test Tests/Xrpl.GenerateEnums.Test/Xrpl.GenerateEnums.Test.csproj --filter "TestURender" --settings test.runsettings -v minimal
```
Expected: PASS (2 tests — `TestURenderTable_ShowsCategoriesAndSummary` and `TestURenderJson_IsParseableAndCarriesSections`).

- [ ] **Step 5: Commit**

```bash
git add Tools/GenerateEnums/Definitions/DiffRenderer.cs Tests/Xrpl.GenerateEnums.Test/TestUDiffRenderer.cs
git commit -m "refactor(tools): neutral node-only label so diff does not assert drift direction"
```
Do NOT add any AI attribution / co-author trailer.

---

### Task 2: The weekly develop-drift monitor workflow

**Files:**
- Create: `.github/workflows/definitions-watch.yml`

- [ ] **Step 1: Create the workflow**

Create `.github/workflows/definitions-watch.yml` with exactly:

```yaml
name: Definitions Watch

# Weekly check that the SDK's definitions.json matches a rippled DEVELOP node's
# server_definitions. Public networks (devnet/testnet/mainnet) all lag develop,
# so this queries the nightly stand — a pinned xrpld `develop` build — where a
# node-only/mismatch genuinely means the SDK is behind develop.
#
# "develop" here = the pinned xrpld version in .ci-config/Dockerfile.nightly, not
# the literal develop tip; the tip moving is covered separately by protocol-watch.

on:
  schedule:
    - cron: '0 7 * * 1' # Mondays 07:00 UTC (one hour after protocol-watch)
  workflow_dispatch:

permissions:
  contents: read

# Only one develop-drift run at a time
concurrency:
  group: definitions-watch
  cancel-in-progress: false

env:
  DOTNET_VERSION: '10.0.x'

jobs:
  watch:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Start rippled develop stand
        run: docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build

      - name: Wait for rippled to be ready
        run: |
          for i in $(seq 1 30); do
            if curl -sf http://localhost:5005/ -d '{"method":"server_info"}' > /dev/null 2>&1; then
              echo "rippled develop stand is ready"
              exit 0
            fi
            echo "Waiting for rippled develop stand... ($i/30)"
            sleep 2
          done
          echo "::error::rippled develop stand did not become ready — dumping diagnostics"
          docker compose -f .ci-config/docker-compose.batchv11.yml ps
          docker compose -f .ci-config/docker-compose.batchv11.yml logs --no-color xrpld
          exit 1

      - name: Use .NET ${{ env.DOTNET_VERSION }}
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      # diff exit codes: 0 in sync (green); 1 node-only/mismatch = SDK behind
      # develop (red); 2 tool error, e.g. stand not reachable (red). The diff
      # table is printed to this step's log so the cause is visible.
      - name: Diff definitions.json against develop
        run: dotnet run --project Tools/GenerateEnums -- diff http://localhost:5005

      - name: Stop rippled develop stand
        if: always()
        run: docker compose -f .ci-config/docker-compose.batchv11.yml down
```

- [ ] **Step 2: Validate the YAML parses**

Run:
```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/definitions-watch.yml')); print('YAML OK')"
```
Expected: `YAML OK`.

- [ ] **Step 3: Sanity-check the referenced paths exist**

Run:
```bash
test -f .ci-config/docker-compose.batchv11.yml && echo "compose OK"
test -f Tools/GenerateEnums/GenerateEnums.csproj && echo "tool OK"
```
Expected: both `OK`. (Confirms the workflow references real files.)

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/definitions-watch.yml
git commit -m "ci(watch): weekly definitions.json vs develop drift monitor"
```
Do NOT add any AI attribution / co-author trailer.

---

### Task 3: End-to-end verification via manual dispatch (post-merge)

This task runs only after the branch is merged to the default branch (GitHub only offers `workflow_dispatch` for workflows on the default branch). It has no committed code — it proves the monitor works end to end.

- [ ] **Step 1: Trigger the workflow manually**

After merge, run:
```bash
gh workflow run "Definitions Watch" --repo StaticBit-io/XrplCSharp --ref dev
```
Expected: the run is queued.

- [ ] **Step 2: Watch the run and read the diff table**

Run (substitute the run id from `gh run list`):
```bash
gh run list --repo StaticBit-io/XrplCSharp --workflow "Definitions Watch" --limit 1
gh run watch <run-id> --repo StaticBit-io/XrplCSharp
```
Expected outcomes, all acceptable as "the monitor works":
- Green (exit 0): `definitions.json` matches the pinned develop stand — "Summary: drift in 0/5 sections".
- Red (exit 1): a real drift — the step log shows the `diff` table with the node-only/mismatch entries (this is the monitor doing its job; the follow-up is a human sync, not a workflow bug).
Only investigate as a workflow defect if it fails with exit 2 (stand never became ready) — then read the dumped `docker compose ... logs xrpld`.

- [ ] **Step 3: Confirm teardown**

The `Stop rippled develop stand` step runs under `if: always()`, so containers are removed even on failure. Confirm the step ran in the run log. No commit.

---

## Notes for the implementer

- **CI + one label line only.** Do not change the `Xrpl` package, the `diff` command's logic, or any version. `generate` is untouched.
- **The label change is behavior-neutral for the wire** — it only alters a human-readable table line; `--json` output and exit codes are unchanged.
- **`workflow_dispatch` needs the workflow on the default branch first** — Task 3 is post-merge, which is why it is a separate task with no commit.
- **The nightly build is the heavy step** (~minutes); it runs weekly, and `if: always()` teardown prevents leaked containers. Do not attempt to make it per-PR.
- **Do not add issue/PR notification** — the chosen reporting mode is fail-the-workflow (a red scheduled run emails watchers); `permissions: contents: read` reflects that.
