# Feature Specification: Accurate AI Usage Telemetry Capture

**Feature short name**: llm-telemetry-capture
**Feature directory**: `specs/016-llm-telemetry-capture`
**Created**: 2026-06-29
**Status**: Draft (ready for planning)

## Why

Spec-009 created the AI telemetry custom fields in Azure DevOps, and the telemetry write-back
(merged in #42) writes a run's metrics onto the work item it produced. But the underlying capture is
incomplete: today the AI usage reported by the provider (tokens, cache savings, errors) is **not
actually recorded**, so the work-item fields the pipeline writes are effectively empty. This feature
makes the capture real and adds the remaining recoverable metrics, so engineering leads can see the
true cost and efficiency of each AI-backed run without guessing.

## Clarifications

### Session 2026-06-29

- Q: Does `AICacheTokens` count cache-read only, cache-read+creation combined, or are both cache figures captured? → A: Capture both `cache_read_input_tokens` and `cache_creation_input_tokens` per call; surface cache-read as `AICacheTokens`; both feed the cost estimate.
- Q: How is cache-hit rate derived (which denominator)? → A: `cache_read / (cache_read + input_tokens) × 100` — the share of prompt input served from cache.

## User Scenarios & Testing

### Primary scenario
As an engineering lead, after a pipeline run creates or updates an Azure DevOps work item, I can open
that work item and see the run's **actual** AI usage — input tokens, output tokens, cache tokens,
estimated cost, AI call count, and how many AI calls errored — so I can track spend and caching
efficiency per piece of work.

### Acceptance scenarios
1. **Given** a run that made one or more AI calls, **when** the run completes and writes telemetry to
   its work item, **then** the input tokens, output tokens, cache tokens, AI call count, and estimated
   cost on the work item reflect the provider's reported usage for that run.
2. **Given** a run that benefited from prompt caching, **when** telemetry is written, **then** a
   cache-hit rate (cached input vs. total input) is shown on the work item.
3. **Given** a run in which an AI call failed at the provider, **when** telemetry is written, **then**
   the work item records a non-zero count of AI errors for the run.
4. **Given** a run with no AI activity (no AI calls made), **when** telemetry is written, **then** the
   usage fields are omitted entirely — never written as zero or fabricated.
5. **Given** the model used has no known pricing, **when** telemetry is written, **then** token counts
   are still written but estimated cost is omitted.

### Edge cases
- The provider response omits usage data → the affected fields are omitted, not zeroed.
- An existing telemetry data store predates the new captured fields → it is upgraded/recreated so the
  new fields are available; absence of the new fields never crashes a run.
- A metric with no real capture source (tool-accept rate) → it is explicitly not written.
- Telemetry capture or write-back fails → the run and the work-item write still succeed.

## Requirements (Functional)

- **FR-001**: The system MUST capture, per AI call, the input tokens, output tokens, cache-read tokens,
  and cache-creation tokens that the LLM provider reports. The work item's `AICacheTokens` field
  reflects **cache-read** tokens (the reuse signal); cache-creation is retained for cost accuracy.
- **FR-002**: The system MUST capture the model identifier the provider reports for each AI call.
- **FR-003**: The system MUST record when an AI call fails at the provider so failures are countable
  per run.
- **FR-004**: Captured per-call telemetry MUST be persisted and associated with the run that produced it.
- **FR-005**: The system MUST aggregate a run's telemetry into run-level totals: input tokens, output
  tokens, cache tokens, AI call count, AI error count, and elapsed AI time.
- **FR-006**: The system MUST write the aggregated telemetry to the run's Azure DevOps work item,
  adding cache tokens, a derived cache-hit rate, and AI error count to the fields already supported.
- **FR-007**: The cache-hit rate MUST be derived as `cache_read / (cache_read + input_tokens) × 100`
  — the percentage of prompt input served from cache (no separate capture needed). When a run made no
  AI calls (zero total input), the rate is omitted rather than reported as 0.
- **FR-008**: Telemetry capture MUST cover both AI calls made by the workflow runner and AI calls made
  by the Spec Kit phase-handler validation.
- **FR-009**: Metrics that have no capture source — specifically tool-accept rate — MUST NOT be written
  or fabricated.
- **FR-010**: Telemetry capture and write-back MUST be non-blocking: a telemetry failure MUST NOT fail
  the run, the validation, or the approved work-item write.

## Success Criteria

- **SC-001**: After a real AI-backed run, every supported telemetry field that has a captured source is
  populated on the work item — none are silently empty.
- **SC-002**: The token counts written to the work item exactly match the provider-reported usage for
  the run.
- **SC-003**: A cache-hit rate is shown for any run that used prompt caching.
- **SC-004**: A run containing a failed AI call records a non-zero AI error count.
- **SC-005**: Zero runs fail and zero work-item writes are lost as a result of telemetry capture across
  a validation period.

## Key Entities

- **AI Call Telemetry** — one record per AI call: run id, model, input tokens, output tokens,
  cache-read tokens, cache-creation tokens, success/failure, duration, timestamp.
- **Run Telemetry Aggregate** — per-run rollup: summed tokens (input/output/cache), AI call count, AI
  error count, elapsed AI seconds, model used; derived cache-hit rate and estimated cost.

## Assumptions

- The LLM provider is the Anthropic Messages API, whose usage payload includes cache token fields.
- "AI error" means a provider call that failed (non-success response or transport exception).
- Tool-accept rate is out of scope — this pipeline has no propose/accept-of-tools concept to measure.
- Adding captured fields to the telemetry data store is acceptable; development data stores will be
  recreated to pick up the new fields (the app provisions schema on startup, not via migrations).

## Out of Scope

- The tool-accept-rate metric.
- Extending the telemetry field configuration to additional work item types (Epic/Bug) — tracked
  separately.

## Dependencies

- Builds on spec-009 (ADO telemetry field bootstrap) and the telemetry write-back merged in PR #42.
