# Contracts: Two-Dimensional AI Cost Tracking

Internal C# seams + one inbound HTTP endpoint. New/changed contracts only.

## C1 — `IBindingKeyMinter` (new, Core)

```csharp
/// Mints a source-neutral, branch-safe binding key for a ticket entering the pipeline.
public interface IBindingKeyMinter
{
    string Mint();                         // e.g. "BIND-7K3QF2AB"
    bool IsValid(string? candidate);       // shape check (branch-safe, non-empty)
}
```
- Minted at signal intake; placed on `PhaseHandlerState.CostBindingKey`.

## C2 — DoR enforcement (`ValidationStep`)

- The validation step MUST fail DoR when `state.CostBindingKey` is absent/invalid (FR-002). Since the
  pipeline mints it at intake, this is a belt-and-suspenders assertion (surfaces a wiring regression).

## C3 — Binding persistence (`CreateWorkItemStep` + connectors)

- On work-item creation, write `Custom.CostBindingKey` (via `IBoardsClient.UpdateFieldsAsync`) and the
  binding key back to the originating ServiceNow ticket (via the ServiceNow connector).

## C4 — `ICostLedger` (new, Core)

```csharp
public interface ICostLedger
{
    Task AppendAsync(CostLedgerEntry entry, CancellationToken ct = default);     // append-only; never throws
    Task<CostTotals> GetTotalsAsync(string bindingKey, CancellationToken ct = default); // {RuntimeUsd, DevUsd}
}
```
- Runtime: one `AppendAsync` per run (anchor work item).
- Development: one `AppendAsync` per ingested session-usage post.
- After append, recompute the work item's cumulative `Custom.AIRuntimeCostUSD` / `Custom.AIDevCostUSD`
  from `GetTotalsAsync` (the ADO-Analytics projection).

## C4b — `IBindingWorkItemMap` (new, resolution — remediation C1)

```csharp
public interface IBindingWorkItemMap
{
    Task PutAsync(string bindingKey, int workItemId, CancellationToken ct = default);   // at creation
    Task<int?> ResolveAsync(string bindingKey, CancellationToken ct = default);          // null = unattributed
}
```
- Populated by `CreateWorkItemStep` when the work item is created; consulted by the dev-usage ingest.
  Avoids per-ingest ADO queries — the pipeline already knows the mapping.

## C5 — Dev-usage ingest endpoint (new, secret-gated)

```
POST /api/telemetry/dev-usage          header: X-Telemetry-Secret
body: DevUsageIngestPayload (see data-model §4)
202 Accepted  { bindingKey, attributed: true|false }
401 when the secret is missing/mismatched
```
- Resolves `binding_key` → work item via **`IBindingWorkItemMap.ResolveAsync`** (C4b); appends a
  `Development` ledger entry. Unresolvable key → `IsUnattributed = true`, `attributed:false` (still 202).
  Re-prices via `ModelPricing` when `cost_usd` is absent. Never throws to the caller beyond auth (best-effort).

## C6 — Rollup (ADO Analytics — documented, not code)

- An ADO Analytics (OData) view / Power BI report sums `Custom.AIRuntimeCostUSD` and
  `Custom.AIDevCostUSD` across the work-item tree (Task→User Story→Feature→Initiative→Project),
  grouped by each level. Delivered as a saved query + report definition, not application code.

## Test contracts (unit, mocked — Article V)

| Unit | Asserts |
|------|---------|
| `IBindingKeyMinter` | minted key is branch-safe + unique; `IsValid` rejects blank/whitespace/slashes |
| `ValidationStep` DoR | missing/invalid binding key → DoR fails |
| `CostLedger` totals | `GetTotalsAsync` sums runtime/dev separately; cumulative across appends; no overwrite |
| Runtime attribution | one run → one entry on the anchor item; multi-item run not duplicated (FR-008) |
| Dev ingest | valid key → attributed Development entry; unknown key → `IsUnattributed`; cost re-priced when absent |
| Per-item projection | cumulative cost fields equal the ledger sums for the binding key |
