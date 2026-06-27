# Phase 1 Data Model: Node Realization

All types are immutable C# records in `DBAIAzure.Core/Models` unless noted. Realized config is
serialized into the **existing** `WorkflowNode.FunctionConfig` (string JSON); no node-graph schema
change. Provenance is stored in the **existing** `WorkflowSettings` blob.

---

## Enums

### `NodeRealizationStatus`
Computed per node (not persisted).

| Value | Meaning |
|-------|---------|
| `Draft` | Plain-language only; no realized config yet. |
| `Proposed` | An LLM proposal exists but the user has not accepted it (transient, session-scoped). |
| `Realized` | Config present, schema-valid, accepted, and (if bound) connector healthy. |
| `Blocked` | Realized form needs a connector that is missing or unhealthy. |
| `NeedsInput` | The LLM could not realize confidently (goal too vague / contradictory). |
| `OutOfDate` | Intent (label/goal/edges) changed after the node was realized. |

### `RealizationDecision`
User action on a proposal: `Accept` · `EditThenAccept` · `Reject` · `Regenerate`.

---

## Per-Node-Type Config Records (`Models/NodeConfig/`)

Each is the typed shape serialized into `FunctionConfig`. A small envelope carries a version +
discriminator so consumers (runtime steps) deserialize the right shape.

### `RealizedNodeConfigEnvelope`
| Field | Type | Notes |
|-------|------|-------|
| `SchemaVersion` | `int` | Starts at 1; guards future migrations. |
| `NodeType` | `WorkflowNodeType` | Discriminator for deserialization. |
| `ConfigJson` | `string` | The serialized type-specific record below. |

### `AgentNodeConfig` (AgenticReason)
| Field | Type | Validation |
|-------|------|-----------|
| `Instruction` | `string` | Required, non-empty (the operating instruction). |
| `ModelRef` | `string` | Required; resolvable model name (defaults to workspace LLM connector model). |
| `OutputShape` | `IReadOnlyList<OutputField>` | If the node feeds a Route/Transform, must be non-empty (FR-14.2). |
| `ToolBindings` | `IReadOnlyList<ConnectorType>` | Optional; each must be a configured connector or the node is `Blocked` (FR-14.3). |

### `OutputField`
`Name` (string), `Kind` (`Text`/`Number`/`Boolean`/`Enum`), `AllowedValues` (`IReadOnlyList<string>?` for Enum).

### `NotifyNodeConfig` (FunctionNotify)
| Field | Type | Validation |
|-------|------|-----------|
| `Connector` | `ConnectorType` | Required; must be a messaging connector (Teams/…); missing/unhealthy → `Blocked`. |
| `RecipientMap` | `string` | Required; how the recipient is derived from upstream output. |
| `MessageTemplate` | `string` | Required; references upstream fields. **Never contains secrets.** |

### `DataNodeConfig` (FunctionData)
| Field | Type | Validation |
|-------|------|-----------|
| `Connector` | `ConnectorType` | Required (ServiceNow/AzureDevOps/…); missing/unhealthy → `Blocked`. |
| `Operation` | `Read` / `Write` | Required. |
| `InputMap` / `OutputMap` | `string` | Required; bind workflow data ↔ the operation. |

### `RouteNodeConfig` (FunctionRoute)
| Field | Type | Validation |
|-------|------|-----------|
| `Conditions` | `IReadOnlyList<RouteCondition>` | Exactly one per outgoing port/edge (FR-15.3). |
| `DefaultPortId` | `string` | Required fallback path. |

### `RouteCondition`
`OutputPortId` (string), `Expression` (string, evaluated against upstream structured output).

### `TransformNodeConfig` (FunctionTransform)
| Field | Type | Validation |
|-------|------|-----------|
| `FieldMappings` | `IReadOnlyList<FieldMapping>` | Non-empty; each maps an upstream field → a downstream-expected field. |

### `ApprovalNodeConfig` (HumanApproval)
| Field | Type | Validation |
|-------|------|-----------|
| `Approver` | `string` | Required (who is asked). |
| `PromptShown` | `string` | Required (what the approver sees). |
| `DecisionOptions` | `IReadOnlyList<string>` | ≥ 2 options (e.g., Approve/Reject). Binds to existing HITL pause/resume. |

### `TriggerNodeConfig` (Trigger)
| Field | Type | Validation |
|-------|------|-----------|
| `InitialDataDescription` | `string` | Required; formalizes the existing `{initialDataDescription}` blob. |
| `OutputShape` | `IReadOnlyList<OutputField>` | Shape of the initial data handed to the first node. |

---

## Realization Session Types

### `RealizationProposal`
A single not-yet-accepted candidate for one node (transient; lives during the review session).

| Field | Type | Notes |
|-------|------|-------|
| `NodeId` | `string` | Target node. |
| `NodeType` | `WorkflowNodeType` | |
| `ProposedConfig` | `RealizedNodeConfigEnvelope` | The candidate config. |
| `PlainLanguageSummary` | `string` | What this node will do / use / produce, for review (FR-16, SC-3). |
| `Status` | `NodeRealizationStatus` | `Proposed`, or `Blocked`/`NeedsInput` if not fully realizable. |
| `BlockingReason` | `string?` | Plain-language reason when `Blocked`/`NeedsInput` (US4). |

---

## Readiness Types

### `NodeReadiness`
`NodeId` (string), `Status` (`NodeRealizationStatus`), `Reasons` (`IReadOnlyList<string>` plain-language).

### `WorkflowReadinessReport`
| Field | Type | Notes |
|-------|------|-------|
| `IsProductionReady` | `bool` | True iff every node is `Realized` (none Blocked/NeedsInput/OutOfDate/Draft). |
| `Nodes` | `IReadOnlyList<NodeReadiness>` | Per-node status + reasons. |
| `BlockingSummary` | `IReadOnlyList<string>` | Top-level reasons the run action is gated (FR-17.4). |

---

## Extensions to Existing Types

### `WorkflowSettings` (extend)
Add `RealizationProvenance` : `IReadOnlyDictionary<string, string>` — `nodeId → intentHash`,
persisted in the existing `SettingsJson`. Mirrors the existing `DesignSkillAnswers` pattern.
`intentHash = SHA256(Label + "" + (GoalPrompt ?? "") + "" + orderedConnectedEdgeSignature)`.

### `WorkflowNode` (no shape change)
- `FunctionConfig` now holds a `RealizedNodeConfigEnvelope` JSON for realized non-trigger nodes
  (Trigger continues to use it, now via `TriggerNodeConfig`).
- `IsConfigured` set `true` only on accepted realization.
- `Label` / `GoalPrompt` (the plain-language layer) are **preserved** alongside realized config
  (FR-16.5).

---

## State Transitions (per node)

```text
Draft ──propose──▶ Proposed ──accept──▶ Realized
  ▲                   │                    │
  │                   ├─reject────────────▶ Draft
  │                   └─(missing connector)▶ Blocked
  │                                          │
  └────────── edit label/goal/edges ◀────────┘  (Realized → OutOfDate)
OutOfDate ──re-realize(node)──▶ Proposed ──accept──▶ Realized
NeedsInput ──user clarifies goal──▶ Proposed
Blocked ──connector configured & healthy──▶ Realized (on re-evaluate)
```

**Validation rules** (enforced by `WorkflowReadinessService`, composing `IWorkflowValidator`):
- VAL-001..003 (existing): exactly one Trigger; no extra Trigger; no island nodes.
- **VAL-004**: every node `IsConfigured` with `FunctionConfig` deserializable to its type's record.
- **VAL-005**: per-type field validation (table above) passes.
- **VAL-006**: every connector-bound node references a configured, healthy connector.
- **VAL-007**: Route has one condition per outgoing edge + a default; agentic node feeding a
  Route/Transform has a non-empty `OutputShape`; downstream input is satisfiable by upstream output.
