# Implementation Plan: Two-Dimensional AI Cost Tracking on the Work Hierarchy

**Feature**: `specs/017-ai-cost-tracking` · **Branch**: `feature/017-ai-cost-tracking`
**Spec**: [spec.md](./spec.md) · **Created**: 2026-06-29

## Summary

Track AI spend as two dimensions — **runtime** (the product pipeline's model calls) and **development**
(engineers' coding-agent sessions) — joined by a **pipeline-minted, DoR-enforced binding key** and
rolled up the ADO work hierarchy. Replace the per-item *snapshot* fields with an append-only **cost
ledger** (cumulative, dimensioned, no duplication), and project cumulative per-item cost into
work-item fields that **ADO Analytics** sums up the Epic→Feature→Initiative tree.

## Technical Context

- **Runtime / framework**: .NET 8, SK Process Framework, EF Core/SQLite (`PipelineDbContext`), Blazor Server, ACA.
- **Builds on**: spec-016 runtime capture (`ILlmUsageReporter`, `WorkflowExecutionEvents`), the #42
  write-back, #44 triggered-by, the DoR phase pipeline (`PhaseHandlerOrchestrator`, `ValidationStep`,
  `CreateWorkItemStep`), the ADO telemetry preflight/manifest, and the ServiceNow + ADO connectors.
- **Clarified decisions**: source-neutral minted binding token; one-run→single-anchor attribution;
  session-level dev binding.

## Constitution Check

| Article | Gate | Verdict |
|---------|------|---------|
| I — Best route | Fix the root model (snapshot→ledger), not a patch | ✅ |
| IV — Code quality | Naming/docs/guard clauses/nullable | ✅ enforced in tasks |
| V — Testing (TDD) | Pure ledger/aggregation/minting/binding logic = 100% mocked unit tests | ✅ |
| VII — **Framework-First** | **Rollup = ADO Analytics (OData), not a custom engine.** Pipeline mint/DoR = SK process steps + events. Ingest = standard ASP.NET controller (mirrors existing webhooks). Ledger = EF table. | ✅ no bespoke infra |
| IX — Secrets | Dev-usage ingest is **secret-gated** like the other webhooks; no secret values in cost data | ✅ |
| X — Verification | Unit tests + a live ingest→ledger→rollup round-trip in quickstart | ✅ |

**Framework-First justification (recorded):** the hierarchy rollup is delegated to **ADO Analytics /
Power BI** over a numeric per-item cost field — we do not build a rollup engine. The app's only job is
to keep those per-item fields *accurate and cumulative* (from the ledger) and to provide the ingest.

## Approach

1. **Binding key — mint at intake, enforce at DoR.** Mint a source-neutral, branch-safe token when a
   ticket enters the pipeline (signal intake), carry it on `PhaseHandlerState`. `ValidationStep` (the
   DoR gate) asserts it is present. `CreateWorkItemStep` writes it to the ADO work item
   (`Custom.CostBindingKey`, queryable) and back to the originating ServiceNow ticket. Lifetime = the
   whole run, so the work item is "born" with its binding.
2. **Cost ledger (new, append-only).** `CostLedgerEntry { BindingKey, Dimension, WorkItemId?, model,
   tokens, cacheTokens, costUsd, occurredAt, sourceId, isUnattributed }`. Both sources append; per-ticket
   total = SUM over the key (cumulative by construction — FR-007). No overwrite.
3. **Runtime dimension.** Per run, write **one** ledger entry tagged with the binding key + the run's
   **anchor** work item (Epic for Plan; single item otherwise) — no per-child duplication (FR-008).
4. **Development dimension.** New secret-gated `POST /api/telemetry/dev-usage` receives a session's
   usage `{ bindingKey, model, tokens, sessionId, … }`, re-prices via `ModelPricing`, appends a
   Development ledger entry. Unresolvable key → entry with `isUnattributed = true` (FR-010).
5. **Per-item projection for Analytics.** Maintain cumulative `Custom.AIRuntimeCostUSD` +
   `Custom.AIDevCostUSD` on the work item (summed from the ledger) so **ADO Analytics** rolls them up
   the tree (FR-009). A documented Analytics/Power BI view delivers "cost by Feature/Initiative."
6. **Best-effort** everywhere (FR-011): ledger/ingest failures never disrupt a run, validation, board
   write, or a developer's session.

## Phase 0 — Research
See [research.md](./research.md). Resolves rollup mechanism, dev-cost source, agent emit path, and the
mint/DoR ordering.

## Phase 1 — Design & Contracts
- [data-model.md](./data-model.md) — binding key, `CostLedgerEntry`, work-item cost fields, ingest payload.
- [contracts/cost-tracking.md](./contracts/cost-tracking.md) — ingest endpoint, minter/ledger seams, ADO Analytics view.
- [quickstart.md](./quickstart.md) — verify mint→DoR→ledger→rollup and the dev-usage ingest.

## Post-Design Constitution Re-check
No new violations. Rollup stays in ADO (Framework-First). Ingest mirrors existing secret-gated webhooks.
Ledger is additive EF. **Gate: PASS.**

## Next
`/speckit-tasks`.
