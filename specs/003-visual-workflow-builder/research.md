# Research: Visual Workflow Builder

**Date**: 2026-06-18 | **Feature**: 003-visual-workflow-builder | **Plan**: [plan.md](plan.md)

---

## Decision 1 — Canvas / Drag-and-Drop Library

**Decision**: Use `Blazor.Diagrams` 3.1.x (`Blazor.Diagrams.Core` + `Blazor.Diagrams`).

**Rationale**: The project is a pure Blazor Server application with no existing JavaScript
dependencies. Blazor.Diagrams is a fully managed Blazor library — it renders via Razor
components, integrates directly with Blazor's component model, and requires no JSInterop
boundary. It provides: a `DiagramCanvas` component, pluggable `NodeModel`/`PortModel`/
`LinkModel` subclasses, built-in drag-and-drop, connection drawing with port snap, zoom/pan,
multi-select, and Blazor template rendering for custom node UI.

**Alternatives considered**:
- **ReactFlow via JS interop**: Industry-leading but adds a JS build pipeline (npm, bundling),
  a JSInterop boundary for every state change, and breaks the pure-Blazor model.
- **JsPlumb / mxGraph via JS interop**: Same interop cost as ReactFlow plus older APIs.
- **Hand-rolled SVG canvas in Blazor**: No third-party dependency but prohibitively complex
  (port snapping, bezier routing, zoom/pan, multi-select all from scratch). Violates Article I.
- **Excalibur.js / Konva.js via interop**: Game/canvas libraries — not purpose-built for
  node-graph UX; significant custom work required.

**Packages**: `Blazor.Diagrams` 3.1.x, `Blazor.Diagrams.Core` 3.1.x (same version).

---

## Decision 2 — Topology Serialization

**Decision**: JSON blob in SQLite — one `WorkflowDefinitionRecord` row per workflow, with
`NodesJson` (array of node objects) and `EdgesJson` (array of edge objects) stored as
`TEXT` columns. `System.Text.Json` is used for serialization (already a transitive dependency).

**Rationale**: SK Process Framework 1.77.0-alpha does not provide stable `KernelProcess`
serialization. The NuGet preview warning `SKEXP0080` on the existing project confirms the API
surface is experimental. Custom JSON representation of the topology (node type, id, position,
port labels, configuration, settings) is the only safe, versionable option. It matches the
existing `ConnectorConfigRecord.ConfigJson` pattern already in `PipelineDbContext`.

**Alternatives considered**:
- **SK Process serialization (built-in)**: Not available in stable form in 1.77.0-alpha.
  Referenced in SK GitHub issues — intended for a future stable release.
- **File-system storage (JSON files)**: Simpler to inspect but cannot be queried for the
  gallery, does not benefit from EF Core transactions, and departs from the established
  storage pattern.
- **Azure Blob Storage**: Over-engineered for a POC; adds a new storage dependency contrary
  to Assumption 4 in the spec.

---

## Decision 3 — Runtime Workflow Execution

**Decision**: Use `ProcessBuilder` constructed dynamically at runtime from the persisted
`WorkflowDefinition`. No custom execution engine.

**Rationale**: SK Process Framework's `ProcessBuilder` API is entirely data-driven. Steps
are registered by type (`AddStepFromType<T>()`), and events are wired via fluent calls that
accept string event IDs and `ProcessFunctionTargetBuilder` instances — both are runtime values,
not compile-time constants. A `WorkflowRuntimeBuilder` class maps each node type to its
corresponding `KernelProcessStep` subclass and generates the event routing graph from the
stored edge list. This is exactly the framework-first approach mandated by Article VII.

**Custom step types** (minimum viable set):
- `AgenticNodeStep` — generic step; reads `GoalPrompt` from step state; calls
  `IChatCompletionService`; emits `NodeCompleted` or `NodeFailed`.
- `FunctionRouteStep` — evaluates a plain-language condition via LLM; emits the matching
  named output event (e.g. `Approved`, `Rejected`).
- `FunctionTransformStep` — calls LLM to reformat/extract from input; emits `NodeCompleted`.
- `FunctionNotifyStep` — deterministic; sends a notification via an injected notifier.
- `FunctionDataStep` — deterministic; reads/writes to configured storage.
- `HumanApprovalStep` — uses existing `IExternalKernelProcessMessageChannel` HITL pattern.

**Alternatives considered**:
- **Custom async execution engine (hand-rolled)**: Unnecessary — SK Process Framework
  already owns this. Violates Article VII.
- **Interpreter over the JSON topology without SK**: Would duplicate SK's event-routing,
  state management, and HITL machinery. Violates Article VII.

---

## Decision 4 — Code Generation

**Decision**: SK `ChatHistory` + `IChatCompletionService` with a structured system prompt
that serializes the visual topology as a human-readable node/edge description, then requests
a complete SK Process Framework code file as output.

**Rationale**: The chat completion service is already registered in DI and proven in
production on this project. Generating code is structurally identical to generating a DoR
verdict — pass a structured prompt, stream the response, capture the output. The topology
serializer converts the `WorkflowDefinition` into a readable description the model can
reason about precisely (node names, goals, connections, settings). Streaming is used
(consistent with existing `ValidationStep` pattern) so the user sees output progressively.

**Code generation target**: SK Process Framework — specifically a `ProcessBuilder`-based
class plus one `KernelProcessStep` subclass per agentic node and one typed events class.
This is immediately compilable as a new file in `DBAIAzure.Processes`.

**Alternatives considered**:
- **Template-based code generation (T4 / Roslyn Source Generators)**: Deterministic but
  cannot handle the user's free-text goals and constraints, which require LLM reasoning.
- **Separate code-gen microservice**: Unnecessary separation; the existing LLM infrastructure
  handles this natively.

---

## Decision 5 — Workflow Design Skill

**Decision**: SK `KernelPlugin` with a `[KernelFunction]` named `AnalyseTopologyAsync` that
accepts the serialized topology and returns a structured JSON list of design questions.
A dedicated `WorkflowDesignSkillService` orchestrates the conversational loop
(question → user answer → next question) using a `ChatHistory` session.

**Rationale**: Using SK `KernelPlugin` is the framework-first approach for structured
analysis functions. The plugin runs the LLM against the topology JSON with a system prompt
that instructs it to identify logical gaps (unterminated loops, missing stop conditions,
ambiguous routing, unconfigured required fields) and return them as structured questions.
The conversational orchestration is a thin wrapper, not a bespoke framework.

**Alternatives considered**:
- **Hardcoded rule-based validation**: Rejected — per spec Clarification Q4, correctness
  enforcement must be conversational and LLM-driven, not hardcoded UI rules.
- **Separate service call for each question type**: Over-decomposed; a single plugin function
  that returns a prioritized list is simpler and produces better cross-question context.

---

## Decision 6 — LLM Availability Monitoring

**Decision**: `ILlmAvailabilityMonitor` — a lightweight service that tracks the last known
LLM state (Available / Unavailable) and exposes a `StateChanged` event. It probes the LLM
with a minimal test call on a background timer (30-second interval) and on every failed
LLM operation. Blazor components subscribe to `StateChanged` to switch between
operational and degraded UI modes without a page reload.

**Rationale**: The spec (FR-05.9) requires automatic restoration of LLM features after
connectivity returns — without a reload. A polling monitor with a `StateChanged` event
mirrors the existing `PipelineOrchestrator.RunUpdated` event pattern exactly.

**Alternatives considered**:
- **Per-component try/catch with retry**: Doesn't meet the "automatic restore without reload"
  requirement; each component would need its own timer.
- **ASP.NET Core Health Checks UI**: Overkill for a single-dependency monitor; adds
  HTTP endpoint exposure unnecessary for this feature.
