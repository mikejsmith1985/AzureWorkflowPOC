# Contracts: Node Realization Services

This feature exposes **internal C# service contracts** (consumed by the Blazor builder UI and
covered by tests) and one **LLM structured-output tool contract**. There are no new HTTP/REST
endpoints. All contracts live in `DBAIAzure.Core/Interfaces` (services) and are realized by
`DBAIAzure.Web/Services` implementations.

---

## C1. `IWorkflowRealizationService`

```csharp
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Uses the LLM to propose executable per-node configuration for a plain-language workflow,
/// and applies user-accepted proposals to the workflow definition. Proposals are reviewed by the
/// user before they take effect (FR-16.1).
/// </summary>
public interface IWorkflowRealizationService
{
    /// <summary>
    /// Proposes configuration for every node that needs it, in graph order. Each proposal is
    /// surfaced to <paramref name="onProposal"/> as it is produced so the UI can show progress
    /// (FR-13.3). Does NOT mutate the workflow — acceptance is a separate, explicit step.
    /// </summary>
    Task<IReadOnlyList<RealizationProposal>> ProposeAllAsync(
        WorkflowDefinition workflow,
        Action<RealizationProposal> onProposal,
        CancellationToken cancellationToken = default);

    /// <summary>Proposes configuration for a single node only (US3) — never touches other nodes.</summary>
    Task<RealizationProposal> ProposeNodeAsync(
        WorkflowDefinition workflow,
        string nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a new workflow with the accepted proposal applied to one node: writes the
    /// (possibly user-edited) config into that node's FunctionConfig, sets IsConfigured = true,
    /// preserves Label/GoalPrompt, and records realization provenance in WorkflowSettings.
    /// Pure/deterministic — no LLM call.
    /// </summary>
    WorkflowDefinition AcceptProposal(
        WorkflowDefinition workflow,
        RealizationProposal proposal,
        RealizedNodeConfigEnvelope acceptedConfig);
}
```

**Contract guarantees**
- `ProposeAllAsync` / `ProposeNodeAsync` are **read-only** w.r.t. the workflow (no mutation, no save).
- A node that cannot be confidently realized returns a proposal with `Status = NeedsInput` or
  `Blocked` and a non-null `BlockingReason` — never a fabricated config (FR-16.3).
- `AcceptProposal` is pure: same inputs → same output workflow; only the one node + settings change
  (SC-5). It throws `ArgumentException` if `acceptedConfig` fails per-type schema validation.
- LLM unavailability surfaces as `LlmUnavailableException` (existing), with partial progress already
  delivered via `onProposal` (Edge Cases).

---

## C2. `IWorkflowReadinessService`

```csharp
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Evaluates whether a workflow is production-ready: structural validity, per-node realized config
/// validity, cross-node consistency, and connector health (FR-17). Async because it performs a
/// live connector health check.
/// </summary>
public interface IWorkflowReadinessService
{
    Task<WorkflowReadinessReport> EvaluateAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);
}
```

**Contract guarantees**
- Composes the existing sync `IWorkflowValidator` (VAL-001..003) + per-type config checks
  (VAL-004..007) + `IConnectorHealthChecker.CheckAllAsync` for connector-bound nodes.
- `IsProductionReady == true` **iff** every node's `Status == Realized` (FR-16.4, FR-17.3).
- Every non-ready node carries ≥ 1 plain-language reason (SC-3, US4).
- Out-of-date detection: recomputes each node's intent hash and compares to
  `WorkflowSettings.RealizationProvenance` (R6).

---

## C3. LLM Structured-Output Tool Contract (per node type)

Each proposal is produced by `IStructuredCompletionService.GetStructuredAsync<TConfig>` with a
forced tool. The tool's `inputSchemaJson` is the JSON Schema of the node type's config record
(data-model.md). Example for a Notify node:

```jsonc
// toolName: "propose_notify_node_config"
// toolDescription: "Propose the production configuration for a notification step."
// inputSchemaJson (shape bound to NotifyNodeConfig):
{
  "type": "object",
  "required": ["connector", "recipientMap", "messageTemplate"],
  "properties": {
    "connector":       { "type": "string", "enum": ["Teams"] },   // only CONFIGURED connectors offered
    "recipientMap":    { "type": "string" },
    "messageTemplate": { "type": "string" }
  }
}
```

**Contract guarantees**
- The `enum` of selectable connectors is built **at call time** from connectors that are actually
  configured in the workspace — the model cannot select an unconfigured connector (FR-16.3).
- The system prompt instructs: derive config from the node's plain-language goal, its I/O labels,
  and upstream/downstream neighbours; if information is insufficient, signal `NeedsInput` rather
  than guessing (Edge Cases).
- Secrets are never included in the schema or prompt (Article IX) — only a `ConnectorType`
  reference is selected.

---

## C4. UI contract (builder)

- A single discoverable **"Make it real"** toolbar action invokes `ProposeAllAsync` (FR-13.1).
- The realization panel renders each `RealizationProposal.PlainLanguageSummary` with
  Accept / Edit-then-Accept / Reject / Regenerate controls (FR-16.2); "Accept all" requires one
  confirmation (FR-16.1).
- Each canvas node shows its `NodeRealizationStatus` badge; the **Run** action is enabled iff the
  latest `WorkflowReadinessReport.IsProductionReady` is true, with blocking reasons surfaced
  (FR-17.4).
- These behaviours are covered by Playwright E2E (quickstart.md).
