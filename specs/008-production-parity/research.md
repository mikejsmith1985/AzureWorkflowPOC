# Phase 0 Research: Production Platform Parity

All spec-level clarifications were resolved in the 2026-06-23 session (see spec **Clarifications**).
This document records the **technical** decisions, each grounded in the existing codebase.

---

## R1. WorkflowRunStatus enum alignment

**Decision**: Keep the existing `WorkflowRunStatus` enum as-is. The spec used `Suspended`/`Succeeded` as conceptual names; the codebase already defines `Paused` and `Completed` which carry identical semantics. No rename. The spec's clarified 6-state model maps to: `Running | Paused | Completed | Failed | TimedOut | Cancelled`. `NotStarted` is retained as an initial-state marker.

**Rationale**: Renaming would break existing `PipelineOrchestrator`, `WorkflowExecutionOrchestrator`, and all UI switch expressions. The existing names are unambiguous and already XML-documented.

**Alternatives considered**:
- Rename to match spec → rejected (churn, no functional gain, breaks existing code).

---

## R2. Process state persistence approach

**Decision**: Persist `WorkflowExecutionRun` snapshots via a new `IWorkflowRunRepository` backed by EF Core. On every status transition in `WorkflowExecutionOrchestrator`, write the run record to the DB. On application startup, rehydrate all `Paused` runs into `_runs` so the Review Queue can surface them. Full SK `IProcessStateManager` (true mid-process revival) is deferred — V1 persistence covers the run record and approval resume path.

**Rationale**: Full SK process state revival (which would allow the background Task to continue from the exact suspended SK step across a server restart) requires a custom `IProcessStateManager` implementation targeting the SK SKEXP0080 API surface, which is still experimental. V1 persistence is sufficient to make the Review Queue reliable and the HITL loop observable. The `ApprovalTcs` in `WorkflowRunState` is recreated on rehydration; the human's decision via `SubmitApproval` re-sends the event into a freshly built process instance (idempotent given the approval node is the entry point on resume).

**Alternatives considered**:
- Custom `IProcessStateManager` for full process blob serialization → deferred (SKEXP0080 surface is experimental; scope risk high for V1).
- In-memory only → rejected (existing behavior; makes HITL demo-only).

---

## R3. Storage provider strategy (SQLite vs Azure SQL)

**Decision**: Retain SQLite for local development. Add a SQL Server / Azure SQL provider path activated by the presence of `Storage:ConnectionString` in configuration. If `ConnectionString` is set, the EF Core `DbContext` uses `UseSqlServer`; otherwise falls back to `UseSqlite` with the existing `SqlitePath`. New tables (`WorkflowRuns`, `WorkflowExecutionEvents`) use EF Core migrations (not raw SQL) to be provider-portable.

**Rationale**: Aligns with spec Assumption 1. Avoids breaking the existing local development workflow. The `Storage:ConnectionString` config key is additive — existing `Storage:SqlitePath` is unchanged.

**Alternatives considered**:
- Migrate entirely to SQL Server → rejected (breaks local dev; no benefit for ≤50 concurrent runs at departmental scale).
- Continue raw-SQL startup migrations → rejected (provider-specific, migration history untrackable).

---

## R4. IHitlNotifier vs new IWorkflowApprovalNotifier

**Decision**: Introduce a new `IWorkflowApprovalNotifier` interface with a signature matching the builder's approval context (`runId`, `workflowName`, `nodeLabel`, `question`, `approverChain`, `decisionOptions`). The existing `IHitlNotifier` serves the pipeline runner and must not be changed. `WorkflowExecutionOrchestrator` injects `IWorkflowApprovalNotifier`; the pipeline orchestrators keep `IHitlNotifier`.

**Rationale**: `IHitlNotifier.NotifyAsync` takes `ticketId`, `questions[]`, and a `portalUrl` — a pipeline-specific shape. The builder's approval step needs `workflowName`, `nodeLabel`, a single `question`, and an `approverChain` list. Unifying them would produce an overloaded interface with nullable fields. Separation keeps each contract focused and testable.

**Alternatives considered**:
- Extend `IHitlNotifier` with overloads → rejected (violates ISP; the pipeline registration would need to handle builder-specific params it doesn't know).

---

## R5. Teams JWT validation on the webhook receiver

**Decision**: Validate the `Authorization: Bearer <token>` header on every inbound action request using the Microsoft Bot Framework JWT validation middleware (`Microsoft.Bot.Connector.Authentication`). The token is issued and signed by Microsoft Teams; the middleware verifies the signature against Microsoft's public key endpoint without any shared secret.

**Rationale**: This is the standard Teams channel authentication pattern (clarification Q1). The Bot Framework auth library handles key rotation automatically. The webhook endpoint returns HTTP 401 on any failure before any business logic executes.

**Alternatives considered**:
- HMAC shared secret in URL path → rejected (manual rotation; weaker than signed JWT).
- Full Azure AD client-credential flow → rejected (too heavyweight for an action-button response; Bot Framework JWT is the correct tier).

---

## R6. IWorkflowObserver architecture

**Decision**: Introduce `IWorkflowObserver` as the single write path for execution events. Two default implementations are registered simultaneously via `IEnumerable<IWorkflowObserver>` injection:
- `SqlWorkflowObserver` — writes `WorkflowExecutionEvent` rows via EF Core.
- `SignalRWorkflowObserver` — forwards live events to the SignalR hub for real-time UI streaming.
A third `AzureMonitorWorkflowObserver` is conditionally registered only when `AzureMonitor:ConnectionString` is configured; if absent, it is not registered (zero-cost no-op by absence, not by null-check).

SK `IFunctionInvocationFilter` and `IPromptRenderFilter` hooks are registered on the kernel factory and call into `IWorkflowObserver` to capture LLM call metadata (model, tokens, latency) without any step-level code changes.

**Rationale**: Decouples DB persistence from live UI streaming from external telemetry. Each concern is independently testable. The Azure Monitor registration guard means dev environments incur zero cost for telemetry they don't have.

**Alternatives considered**:
- Single observer with conditional branches → rejected (violates SRP; hard to unit test).
- Write directly from steps → rejected (couples steps to infrastructure; violates SK step purity).

---

## R7. DoR rule registration

**Decision**: Implement `IWorkflowReadinessRule` as a strategy interface (`CheckAsync(WorkflowDefinition, IReadOnlyList<ConnectorInstance>) → RuleResult`). Rules are registered via DI (`services.AddWorkflowReadinessRule<T>()`). The `WorkflowPreRunValidator` resolves `IEnumerable<IWorkflowReadinessRule>` and evaluates them in order. Rule enable/disable is managed via a `DorRuleSettings` configuration section (JSON array of rule type names that are disabled).

**Rationale**: Matches the spec's FR-24.3 (admin can enable/disable rules without deployment) while keeping rules as registered types (not database rows), which is simpler at departmental scale. Configuration reload at runtime is handled by `IOptionsMonitor<DorRuleSettings>`.

**Alternatives considered**:
- Rules stored as database rows → rejected (over-engineered at ≤50 runs scale; requires admin UI for rule authoring not in scope).
- Hardcoded switch in validator → rejected (violates FR-24.4, not extensible).

---

## R8. Whole-workflow generation from chat

**Decision**: Add `GenerateWorkflowAsync(string description, CancellationToken)` to `WorkflowDesignSkillService` (the existing LLM-loop service). The method uses `IStructuredCompletionService` with a JSON schema for `WorkflowGenerationResult` (a list of nodes with types, labels, goals, and a list of edges). The result is returned to the Blazor page which renders it onto the canvas via the existing `WorkflowBuilderService`.

**Rationale**: Mirrors the existing `WorkflowDesignSkillService` pattern (reuse the LLM loop, reuse `IStructuredCompletionService`). The output schema is a subset of `WorkflowDefinition` — no new graph types needed. FR-23.3 (one clarifying question on ambiguity) is handled by the LLM's system prompt instructing it to ask before generating.

**Alternatives considered**:
- Separate `ChatWorkflowGeneratorService` → rejected (same LLM and structured output primitives; duplication without benefit).
- Free-text generation + parsing → rejected (violates Article VII structured output mandate).
