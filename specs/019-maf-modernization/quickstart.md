# Quickstart: Validate the MAF Modernization

**Feature**: `specs/019-maf-modernization` · **Date**: 2026-07-12

This guide proves the migration end-to-end against the spec's success criteria. It is a **validation/run
guide** — implementation lives in `tasks.md`. Evidence (constitution Article X) is a passing suite plus
observed runtime behavior, not "it compiles."

## Prerequisites

- .NET 8 SDK (user-local, resolved via `global.json`).
- Anthropic/Claude credentials injected via the vault (default provider). No other AI subscription needed.
- Run the app with the user-local dotnet (has the ASP.NET runtime):
  `~/.dotnet/dotnet.exe` or `C:\Users\<you>\AppData\Local\Microsoft\dotnet\dotnet.exe`.

## Setup

```bash
# packages present (target execution path — all GA):
#   Microsoft.Agents.AI, Microsoft.Agents.AI.Workflows, Microsoft.Extensions.AI, Anthropic
# and ZERO Microsoft.SemanticKernel* references remain.
grep -rEl "Microsoft\.SemanticKernel" --include=*.csproj src/     # expect: no matches (SC-002)
grep -rn "SKEXP0080" src/                                          # expect: no matches (FR-003)
```

## Validation scenarios

### 1. No experimental packages remain (SC-002 / FR-003)
- **Do**: inspect `.csproj` files + source for `Microsoft.SemanticKernel*` and `SKEXP0080`.
- **Expect**: zero matches in the execution path; only GA packages (research.md package summary).

### 2. Three pipelines behave identically (SC-001 / FR-001/002/004)
- **Do**: run the full suite — `./scripts/run-e2e.ps1` plus `dotnet test` — and drive each pipeline:
  ticket intake, phase-handler, a multi-node visual workflow (agentic/route/transform/notify/data/approval).
- **Expect**: 100% of existing tests pass with none deleted; same steps, same route/port selection, same
  work items, same run history as the pre-migration build.

### 3. Human-in-the-loop pause/resume, incl. restart (SC-003 / FR-005/006)
- **Do**: trigger each HITL surface (intake console prompt, phase-handler approval card, visual Review
  Queue). For one, **restart the app while a run is paused**, then approve.
- **Expect**: run suspends, item appears, decision recorded, run resumes from the correct point — and the
  restarted run rehydrates from its checkpoint and still resolves.

### 4. Cost/telemetry parity, tagged by provider (SC-004 / FR-010/009e)
- **Do**: execute model-using runs; compare captured token counts + computed cost to the pre-migration
  build for equivalent inputs; inspect the ledger rows.
- **Expect**: identical accounting (0% delta); each row tagged with provider + model; binding key unchanged.

### 5. Streaming preserved (FR-011a)
- **Do**: open Run Detail → **Stream** tab during a run.
- **Expect**: live token streaming, equivalent to today (not just final output).

### 6. Bring-your-own-AI (SC-008 / US6)
- **Do**: with only Claude configured, run end-to-end. Then set `AI:Provider` to a second registered
  `IChatClient` adapter (+ its credentials) and re-run. Then set an unknown provider.
- **Expect**: (a) runs on Claude with no other subscription; (b) same run executes on the new provider
  with **zero** pipeline/step code change; (c) unknown provider → clear error **naming the provider**, no
  silent fallback.

### 7. Observability to Azure Monitor (SC-006 / FR-013)
- **Do**: execute a run; check Azure Monitor / App Insights for orchestration + model-call spans.
- **Expect**: spans present under the new MAF/M.E.AI source names; no coverage gap vs. today.

### 8. Paused-run migration (SC-009 / FR-006a)
- **Do**: against a copy of pre-cutover data containing SK-paused runs, run the one-time migration, then
  approve those runs.
- **Expect**: 100% auto-migrate and resume from their pause point; zero lost approvals.

### 9. Performance budget (SC-010 / FR-014a)
- **Do**: measure end-to-end run latency + per-model-call overhead for a representative set vs. the
  pre-migration baseline on the same host/model.
- **Expect**: regression ≤10%; a larger regression blocks cutover.

## Done / cutover gate
All nine scenarios pass → the atomic cutover release (remove `Microsoft.SemanticKernel*`, ship the
paused-run migration) is authorized. Any failure blocks the switch.
