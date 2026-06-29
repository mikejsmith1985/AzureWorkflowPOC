# Quickstart: Verify Two-Dimensional AI Cost Tracking

Proves mint → DoR → ledger → rollup, plus the dev-usage ingest. Implementation lives in `tasks.md`.

## Prerequisites
- LLM + ADO + ServiceNow connectors configured; ADO telemetry preflight has run (so `Custom.CostBindingKey`,
  `Custom.AIRuntimeCostUSD`, `Custom.AIDevCostUSD` exist or have Adaptive fallbacks).
- `WebhookSecrets:Telemetry` configured for the dev-usage endpoint.
- Fresh `pipeline.db` (the `CostLedgerEntry` table is provisioned by `EnsureCreated`).

## Unit-test gate (fast, no I/O)
```
$DOTNET_ROOT/dotnet.exe test tests/DBAIAzure.Tests/DBAIAzure.Tests.csproj --filter "FullyQualifiedName~CostTracking|FullyQualifiedName~BindingKey|FullyQualifiedName~CostLedger"
```
Expected: minter, DoR-enforcement, ledger-totals, runtime-attribution, dev-ingest, projection tests green.

## Scenario A — Binding minted & DoR-enforced
1. Send a phase signal. Inspect the run: `CostBindingKey` is present on state from intake.
2. (Negative) Force a blank binding key → `ValidationStep` fails DoR. Confirms FR-002.

## Scenario B — Runtime cost lands once on the anchor
1. Run a **Plan** phase that creates several Tasks.
2. Expected: exactly **one** `Runtime` ledger entry for the run, on the **anchor** (Epic); summing the
   tree does not multiply by the Task count (FR-008). The Epic's `Custom.AIRuntimeCostUSD` reflects it.

## Scenario C — Development cost via ingest
1. `POST /api/telemetry/dev-usage` with `X-Telemetry-Secret` and a body carrying a **valid** binding key
   + token counts.
2. Expected: a `Development` ledger entry; the bound work item's `Custom.AIDevCostUSD` increases by the
   re-priced amount. Re-post → it **accumulates** (FR-007), never overwrites.
3. (Unattributed) Post with an unknown binding key → entry recorded with `IsUnattributed = true`,
   response `attributed:false`; nothing dropped (FR-010).

## Scenario D — Rollup up the hierarchy
1. In ADO Analytics (or the Power BI report), open the saved cost view.
2. Expected: a Feature/Initiative shows runtime + dev totals equal to the sum of its descendants'
   per-item cost fields, with the two dimensions distinguishable (SC-001, SC-006).

## Non-blocking checks
- Ledger/ingest failure → the run, validation, board write, and dev session all still succeed (FR-011).
