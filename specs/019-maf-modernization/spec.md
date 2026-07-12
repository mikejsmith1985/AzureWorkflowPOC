# Feature Specification: Modernize the Agent Stack onto Microsoft Agent Framework (MAF)

**Feature short name**: maf-modernization
**Feature directory**: `specs/019-maf-modernization`
**Created**: 2026-07-12
**Status**: Draft — ready for `/speckit-plan`

## Context

The platform's orchestration core is built on **Semantic Kernel (SK)** and, specifically, the **SK
Process Framework**. Today the solution pins:

- `Microsoft.SemanticKernel` **1.77.0** (stable), and
- `Microsoft.SemanticKernel.Process.Core` / `Microsoft.SemanticKernel.Process.LocalRuntime`
  **1.77.0-alpha** — an **experimental, pre-release** API surface consumed under the
  `#pragma warning disable SKEXP0080` opt-out.

Every one of the three runtime pipelines — **ticket intake**, **phase-handler**, and the **visual
workflow builder** — is assembled and executed through this experimental Process Framework (17 step
classes, 3 graph builders, proxy-step human-in-the-loop, and an in-memory local runtime the app wraps
with its own database-backed pause/resume). The LLM layer is a hand-rolled Anthropic connector that
implements SK's `IChatCompletionService`, plus two SK kernel filters that capture the AI cost and
telemetry data shipped in prior features (spec-016 / spec-017).

Microsoft has since shipped **Microsoft Agent Framework (MAF) 1.0**, the general-availability
convergence of Semantic Kernel and AutoGen (GA on 2026-04-03 for .NET and Python). MAF is the
**primary, long-term-supported** foundation going forward: new investment lands in MAF, while SK v1.x
moves to maintenance (critical fixes and security patches, supported for at least one year after MAF
GA). MAF supersedes the SK Process Framework with a native, generally-available **Workflows** model
(graph-based orchestration with first-class checkpointing and request/response human-in-the-loop), and
standardizes the model layer on `Microsoft.Extensions.AI` (`IChatClient`) with a single consolidated
agent type.

**The problem this feature solves**: the platform's most critical subsystem — workflow orchestration —
currently depends on an **experimental alpha** API that is not the long-term-supported path. That is a
standing production and support risk. This feature modernizes the stack onto MAF's
generally-available foundation **without changing what the application does for its users**, and takes
the opportunity to revisit the directly-adjacent components (the LLM connector, structured output, the
telemetry filters, MCP tool delivery, and observability wiring) that are coupled to SK today.

## Clarifications

### Session 2026-07-12

- **Q: Does "replace anything that is not Microsoft's primary long-term solution" include switching the
  model vendor away from Anthropic/Claude to Azure OpenAI / Foundry?**
  **A:** No. "Primary long-term solution" refers to the **agent framework** (MAF replacing SK), not the
  model vendor. The application is deliberately built on Claude (dedicated connector, project
  constitution, live-verify secrets). What is modernized is the **abstraction** the app uses to reach
  the model (SK `IChatCompletionService` → `Microsoft.Extensions.AI` `IChatClient` behind MAF).

- **Q: Should the model provider be fixed to Anthropic, or configurable?**
  **A:** **Bring your own AI (BYO-AI).** Anthropic/Claude remains the **default** provider and the only
  one that must be configured for the product to run out of the box (no Azure OpenAI / OpenAI
  subscription is required). But because the modern client abstraction (`IChatClient`) is
  provider-neutral, the application MUST let an operator **configure which provider and model** it uses,
  and adding another provider (e.g., Azure OpenAI, OpenAI, a local/self-hosted model) MUST be a
  configuration + adapter concern, not a change to the orchestration core. See US6.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Orchestration runs on the supported foundation with no behavior change (Priority: P1) 🎯

As the platform owner, I want all three pipelines to run on Microsoft's generally-available agent
foundation instead of the experimental Process Framework, so that the product no longer ships on a
pre-release API and stays on Microsoft's supported path — while every existing behavior is preserved.

**Independent Test**: Run the ticket-intake, phase-handler, and visual-workflow pipelines end-to-end
against the same inputs used today; each produces the same observable outcomes (same steps executed,
same routing decisions, same work items created, same run history) as the pre-migration build, and the
existing automated test suite passes unchanged.

**Acceptance Scenarios**:

1. **Given** a ticket-intake run that today produces a specific set of clarifying questions, an
   estimate, and a created work item, **When** the same run executes on the modernized stack, **Then**
   the observable outcome is equivalent and the run history records the same step sequence.
2. **Given** the visual workflow builder with a saved multi-node workflow (agentic, route, transform,
   notify, data, and human-approval nodes), **When** it is executed, **Then** each node runs, routing
   selects the same port, and the run completes or pauses exactly as before.
3. **Given** the solution's dependency manifest, **When** the orchestration runtime is inspected,
   **Then** it depends only on generally-available, long-term-supported components — no
   experimental/pre-release orchestration package and no experimental-API opt-out pragma remain in the
   execution path.

### User Story 2 - Human-in-the-loop pauses and resumes exactly as today (Priority: P1)

As an approver, I want every human-in-the-loop gate to keep working — pause the run, wait for my
decision, and resume — so that the migration is invisible to the people who review and approve work.

**Independent Test**: Trigger each of the three HITL surfaces (intake console prompt, phase-handler
approval card, visual-builder Review Queue), confirm the run suspends, the pending item appears, a
decision is recorded, and the run resumes from the correct point — including across an application
restart for the persisted surfaces.

**Acceptance Scenarios**:

1. **Given** a phase-handler run that reaches an approval gate, **When** the run pauses, **Then** an
   approval item appears in the Review Queue and the run remains suspended until a decision is made.
2. **Given** a suspended run and an application restart, **When** the app comes back up, **Then** the
   run is rehydrated and can still be approved or rejected, resuming from where it paused.
3. **Given** an approval that is never actioned, **When** the configured timeout elapses, **Then** the
   run auto-resolves exactly as it does today (e.g., auto-reject / escalate).

### User Story 3 - AI usage is reached through the modern client and still fully metered (Priority: P2)

As the finance/operations owner, I want the AI cost and telemetry capture delivered in the prior
features to keep working after the LLM layer is modernized, so that per-run and developer AI costs
remain accurate and bound to work items.

**Independent Test**: Execute runs that invoke the model, then confirm token usage, cost ledger
entries, and the run→work-item cost binding are recorded identically to the pre-migration build, and
that the model is now reached through the modern client abstraction.

**Acceptance Scenarios**:

1. **Given** a run that calls the model several times, **When** it completes, **Then** the captured
   token counts and computed cost match what the current pipeline records for the same run.
2. **Given** the modernized LLM layer, **When** a model call is made, **Then** it flows through the
   standardized client interface (not the retired SK chat-completion service) and the hot-reload of
   model/key from configuration still applies per call.

### User Story 4 - Tool delivery (MCP) keeps working through the agent (Priority: P3)

As a workflow author, I want steps that deliver messages or call tools via Model Context Protocol to
keep working after the migration, so that connector-backed actions are unaffected.

**Independent Test**: Run a workflow whose step delivers through the MCP gateway; confirm the tool call
executes and the message is delivered as before.

### User Story 5 - Traces still reach Azure Monitor under the new framework (Priority: P3)

As an operator, I want distributed traces and metrics to keep flowing to Azure Monitor after the
migration, so that observability of runs is not lost — even though the framework's trace source names
change.

**Independent Test**: Execute a run and confirm spans for the orchestration and model calls appear in
Azure Monitor / Application Insights, sourced from the new framework's telemetry, with no gap versus
the current build.

### User Story 6 - Bring your own AI (configurable model provider) (Priority: P2)

As the person deploying the platform, I want to choose which AI provider and model the application uses
through configuration, so that I can run it on Claude out of the box today and later point it at a
different provider (Azure OpenAI, OpenAI, or a self-hosted model) **without a subscription I don't want
and without changing the orchestration core**.

**Independent Test**: With only Claude credentials configured, the app runs end-to-end. Then, by
changing configuration to select a different configured provider (and supplying that provider's
credentials), the same runs execute against the new provider — with no change to pipeline/step code.

**Acceptance Scenarios**:

1. **Given** a fresh deployment with only Anthropic/Claude credentials, **When** the app starts,
   **Then** it runs with Claude as the default provider and requires **no** other AI subscription.
2. **Given** configuration that selects a different provider and supplies its credentials, **When** a
   run executes, **Then** model calls go to the selected provider and the run behaves equivalently
   (subject to the chosen model's capability).
3. **Given** an operator wants to add a provider the app has not shipped before, **When** they supply
   an `IChatClient`-compatible adapter and configuration, **Then** it is usable **without** modifying
   the pipelines, steps, or orchestration engine.
4. **Given** a selected provider is misconfigured or unreachable, **When** the app resolves the model
   client, **Then** it fails with a clear, actionable message naming the provider — it does not silently
   fall back to a different provider.

### Edge Cases

- A run that was **persisted while paused on the old stack** must remain resolvable after the upgrade,
  or a documented, one-time migration/backfill path must exist. (Cross-version resume.)
- A step that **fails or times out** mid-run must surface the same error/paused state and Review-Queue
  behavior as today.
- **Structured (schema-bound) outputs** — routing decisions and node realization currently rely on
  forced-tool-use JSON bound to typed records; the modernized layer must produce the same typed results
  or the run's routing/realization changes, which is a regression.
- **Concurrent runs** must remain isolated (no shared-state bleed) under the new runtime.
- If MAF's model layer cannot reach Anthropic through the standard client, a documented adapter must
  exist so the model vendor is genuinely unchanged.

## Requirements *(mandatory)*

### Functional Requirements

**Orchestration core**

- **FR-001**: All three pipelines (ticket intake, phase-handler, visual workflow builder) MUST execute
  on Microsoft Agent Framework's generally-available orchestration model, replacing the SK Process
  Framework as the execution engine.
- **FR-002**: The migrated pipelines MUST preserve existing observable behavior — same steps, same
  branching/routing outcomes, same created work items, same run-history records — for equivalent
  inputs (no functional regression).
- **FR-003**: The orchestration execution path MUST NOT depend on any experimental or pre-release
  package, and MUST NOT retain experimental-API opt-out pragmas (e.g., `SKEXP0080`).
- **FR-004**: The visual workflow builder MUST continue to translate a saved workflow definition into a
  runnable orchestration at runtime, mapping each node to a step/executor and each edge to a transition,
  and exposing route/port selection equivalently.

**Human-in-the-loop**

- **FR-005**: All three human-in-the-loop surfaces MUST continue to suspend the run, surface the
  pending decision (console prompt, approval card, Review Queue), and resume from the correct point on
  decision.
- **FR-006**: Persisted paused runs MUST survive an application restart and remain resolvable
  (rehydration), preserving the current durable pause/resume guarantee.
- **FR-007**: Existing approval timeout / escalation / auto-resolution behavior MUST be preserved.

**LLM layer & metering**

- **FR-008**: Model access MUST be modernized to the standardized client abstraction
  (`Microsoft.Extensions.AI` `IChatClient`) used by MAF, replacing the SK `IChatCompletionService`
  surface. The orchestration/step code MUST depend only on this provider-neutral abstraction, not on
  any provider-specific client type.
- **FR-009**: Per-call hot-reload of model id and API key from configuration MUST be preserved.

**Bring your own AI (configurable provider)**

- **FR-009a**: The application MUST select its AI **provider and model from configuration**, defaulting
  to **Anthropic/Claude**. The product MUST run out of the box with only Claude credentials configured
  and MUST NOT require any other AI subscription.
- **FR-009b**: Adding or selecting a different provider (e.g., Azure OpenAI, OpenAI, self-hosted) MUST
  be achievable by supplying an `IChatClient`-compatible adapter plus configuration and credentials —
  **without** modifying pipelines, steps, or the orchestration engine.
- **FR-009c**: Provider credentials MUST be resolved by reference from configuration/secrets (never
  hard-coded), consistent with the existing secrets discipline (constitution Article IX), with each
  provider's secrets named independently.
- **FR-009d**: If the selected provider is misconfigured or unreachable, the application MUST fail with
  a clear message that **names the provider** and MUST NOT silently fall back to another provider.
- **FR-009e**: AI cost/telemetry capture (FR-010) MUST record which provider and model produced each
  run's usage, so cost accounting remains correct across providers.
- **FR-010**: AI cost and telemetry capture (token usage, cost ledger, run→work-item cost binding from
  spec-016 / spec-017) MUST continue to function, re-homed onto the new framework's interception/
  middleware mechanism in place of the SK kernel filters.
- **FR-011**: Structured, schema-bound outputs (routing decisions, node realization) MUST produce the
  same typed results as today.

**Complementary components**

- **FR-012**: MCP-based tool/message delivery MUST continue to function through the agent/tool model of
  the new framework.
- **FR-013**: Distributed tracing and metrics MUST continue to reach Azure Monitor, with the telemetry
  source configuration updated to the new framework's source names so no observability gap results.
- **FR-014**: Tool exposure that today requires the `[KernelFunction]` attribute MUST be re-expressed
  in the new framework's tool model without loss of any currently-exposed tool.

**Migration integrity**

- **FR-015**: The migration MUST be verifiable against the existing automated test suite (unit,
  component, and end-to-end), which MUST pass; where a test asserts an SK-specific type or symbol, it
  MUST be updated to assert the equivalent behavior, not deleted to hide a gap.
- **FR-016**: Any SK component retained temporarily for interop MUST be documented with the reason and
  the removal condition, so the end state is a single, consistent agent stack rather than a permanent
  hybrid.
- **FR-017**: `CHANGELOG.md` MUST record the modernization; the project's Framework-First guidance
  (constitution Article VII) MUST be updated to name MAF as the governing framework in place of the SK
  Process Framework.

### Key Entities *(include if feature involves data)*

- **Pipeline** — a runnable orchestration (intake / phase-handler / visual workflow). Migrates from an
  SK `KernelProcess` graph to a MAF workflow.
- **Step / Node** — a unit of work in a pipeline (LLM reasoning, routing, transform, notify, data,
  human-approval, terminal create). Migrates from `KernelProcessStep` to the MAF executor/step model.
- **Run** — a single execution instance with status, history, snapshots, token/cost records, and a
  possible paused state. Its persisted shape and durable pause/resume semantics must be preserved.
- **Approval / Review item** — a pending human decision that suspends a run. Behavior preserved across
  all three surfaces.
- **Cost/telemetry record** — token usage and cost bound to a run and work item. Capture path re-homed
  but data preserved.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: **Zero functional regressions** — 100% of the existing automated tests (unit, component,
  E2E) pass after migration, with no test deleted to accommodate the change.
- **SC-002**: The orchestration runtime depends on **zero** experimental/pre-release packages and
  retains **zero** experimental-API opt-out pragmas.
- **SC-003**: All **three** HITL surfaces demonstrably pause and resume, including at least one
  resume **across an application restart**.
- **SC-004**: For a representative set of runs, captured **token counts and computed cost match the
  pre-migration build within 0%** (identical accounting) for equivalent inputs.
- **SC-005**: Model calls are made through the standardized client interface in **100%** of LLM-using
  code paths (no remaining dependency on the retired SK chat-completion service in the execution path).
- **SC-006**: Distributed traces for orchestration and model calls appear in Azure Monitor for a test
  run with **no coverage gap** versus the current build.
- **SC-007**: The end state contains a **single** agent framework in the execution path; any interim
  interop shim is documented with a removal condition.
- **SC-008**: With only Claude credentials configured, the product runs end-to-end (**no other AI
  subscription required**); and switching the active provider is achievable by **configuration only**,
  with **zero** changes to pipeline/step/orchestration code, demonstrated by pointing at least one
  additional `IChatClient` adapter at a representative run.

## Assumptions

- **Claude is the default; provider is pluggable (BYO-AI)**: Anthropic/Claude remains the **default**
  and the only provider that must be configured to run out of the box — **no Azure OpenAI / OpenAI
  subscription is required**. "Microsoft's primary long-term solution" is interpreted as the **agent
  framework** (MAF), not the model vendor. Modernizing to the provider-neutral `IChatClient` abstraction
  is what makes bring-your-own-AI a configuration concern rather than a code change. The app ships with
  the Claude adapter; other providers are enabled by configuration + an `IChatClient` adapter.
- **MAF Workflows is the Process Framework successor** used for graph-based orchestration, providing
  native checkpointing and request/response human-in-the-loop; the app's existing database-backed
  rehydration may be simplified onto it where equivalent, but the durable-pause guarantee is the
  requirement, not the mechanism.
- **Incremental, low-risk migration** is preferred over a big-bang rewrite: pipelines may be migrated
  one at a time, using the documented SK↔MAF interop path during the transition, provided the end state
  is a single stack (FR-016).
- **.NET 8** and the existing hosting model (Blazor Server web app + console runner) are retained.
- The existing **Review Queue, SignalR run updates, and run persistence** remain the app-level
  constructs they are today; only their coupling to SK primitives changes.
- MAF can reach Anthropic through an `IChatClient` implementation (native or a thin adapter); if not
  native, providing that adapter is part of this feature (FR-008).

## Out of Scope

- **Shipping** additional provider adapters beyond Claude (e.g., a built-in, tested Azure OpenAI or
  OpenAI adapter). BYO-AI requires the app to *support* pluggable providers via `IChatClient` and to not
  preclude them (FR-009a–e); authoring and validating specific extra adapters is a follow-on. No AI
  subscription other than Anthropic is required or assumed by this feature.
- Replacing non-agent third-party libraries that have no Microsoft-primary agent-stack equivalent and
  are not coupled to SK: the visual canvas (`Z.Blazor.Diagrams`), diffing (`DiffPlex`), console UI
  (`Spectre.Console`), container control (`Docker.DotNet`), and the Azure DevOps client
  (`Microsoft.TeamFoundationServer.Client`). These may be revisited in a separate initiative.
- New user-facing features or UI redesigns — this is a modernization with **no** behavior change.
- Migrating persistence off EF Core, or changing the SQLite/SQL Server storage design.
- Replacing the test stack (xUnit / bUnit / Playwright).

## Dependencies

- Microsoft Agent Framework 1.0 (`Microsoft.Agents.AI`) and `Microsoft.Extensions.AI`.
- Continued availability of the Anthropic Messages API and existing vault-injected secrets.
- Prior features whose behavior must be preserved: spec-007 (node realization), spec-010 (messaging/MCP),
  spec-013 (app monitoring runs), spec-016 (LLM telemetry), spec-017 (AI cost tracking),
  spec-018 (work-tracker adapter).
