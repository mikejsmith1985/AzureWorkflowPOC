# Contract: IWorkTrackerAdapter

The single seam the pipeline / cost / binding layers depend on. One implementation per tracker. All
operations are **best-effort** — they log and swallow rather than throw into a pipeline run (FR-012).

## C1 — `IWorkTrackerAdapter`

```csharp
public interface IWorkTrackerAdapter
{
    string TrackerKey { get; }   // "AzureDevOps" | "Jira" — for diagnostics/config

    Task<WorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description,
        WorkItemRef? parent, CancellationToken ct = default);

    Task UpsertWorkItemAsync(
        WorkItemRef item, string title, string description, string appendComment,
        CancellationToken ct = default);

    Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default);

    // Fields are keyed by tracker-neutral LogicalField names; the adapter resolves to native refs.
    Task SetFieldsAsync(
        WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields,
        CancellationToken ct = default);

    // Resolve a supplied binding key to its work item (null = unattributed). May use a tracker query
    // or defer to the local IBindingWorkItemMap — see C3.
    Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default);

    // Ensure the logical fields exist and are usable on the relevant item types — idempotent.
    Task<ProvisioningResult> ProvisionFieldsAsync(
        AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default);

    RollupCapability GetRollupCapability();
}
```

> **Binding-key write** (spec FR-002) is **not** a separate method — it is `SetFieldsAsync` with
> `LogicalField.CostBindingKey`. Writing it as a field is what makes the key resolvable on the tracker
> (e.g. Jira JQL `cf[...]`), so the resolution edge (`ResolveByBindingKeyAsync`) depends on it.

## C2 — `IWorkTrackerAdapterProvider` (the resolution seam)

```csharp
public interface IWorkTrackerAdapterProvider
{
    // routingContext is null in v1 (single active tracker); reserved for per-project routing (FR-005).
    IWorkTrackerAdapter GetAdapter(WorkRoutingContext? routingContext = null);
}
```

## C3 — Behaviour every adapter MUST honour (contract tests)

| Behaviour | Requirement |
|-----------|-------------|
| Create returns a usable ref | `CreateWorkItemAsync` returns a `WorkItemRef` that subsequent `SetFieldsAsync`/`ResolveByBindingKeyAsync` accept. |
| Parent link | A non-null `parent` links the new item under it in the tracker's hierarchy. |
| Logical field set | `SetFieldsAsync` writes each logical field to its native ref; unknown/unprovisioned fields are skipped + logged, not thrown. |
| Binding resolution | `ResolveByBindingKeyAsync` returns the work item whose binding key matches; an unknown key → `null` (→ unattributed). |
| Idempotent provisioning | A second `ProvisionFieldsAsync` makes no changes and still reports `IsSuccess`. |
| Best-effort | No adapter method throws into the run on a tracker/permission failure — it returns a failure result / logs. |
| Rollup honesty | `GetRollupCapability` returns `Native` only when the tracker sums the cost fields up the tree without an add-on; otherwise `RequiresAddOn`/`None` with a notice. |

## C4 — ADO adapter (`AzureDevOpsWorkTrackerAdapter`)

Delegates to the existing `AzureDevOpsBoardsClient` + `AdoTelemetryPreflightService` (unchanged). Maps
`WorkItemRef ↔ int`, logical → `Custom.<logical>`, logical type → ADO WIT (via the existing preflight,
incl. the #46/#47 inherited-process handling). `GetRollupCapability` → `Native("ADO Analytics")`. Must be
behaviourally identical to today (SC-001).

## C5 — Jira adapter (`JiraWorkTrackerAdapter`)

Jira Cloud REST. Create issue (`POST /rest/api/3/issue`, parent via parent field / Epic link). Set fields
by resolved `customfield_*`. Resolve binding via JQL `cf[<bindingFieldId>] ~ "<key>"` (or the local map).
Provision via `JiraFieldProvisioner` (find-or-create field → context to issue types+project → screens).
`GetRollupCapability` → `Native("Advanced Roadmaps")` when present, else `RequiresAddOn`.

## C6 — Contract-test matrix

A shared xUnit theory runs the C3 behaviours against each registered adapter (ADO with a fake ADO client,
Jira with a fake Jira REST handler) so both implementations are held to the identical contract.
