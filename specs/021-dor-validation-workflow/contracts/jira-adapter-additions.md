# Contract: Work-Tracker Adapter Additions (Jira read + transition)

Two additive capabilities behind the existing tracker-neutral `IWorkTrackerAdapter` seam (spec-018). Reuse
`IJiraConnectionFactory` (per-call hot-reload credentials) inside `JiraWorkTrackerAdapter`. ADO/other trackers
implement or no-op these additively (Framework-First; D5).

## Interface additions

```csharp
public interface IWorkTrackerAdapter   // additions only
{
    // Reads a work item into a normalized field map for the review payload. Keys are logical/field ids per
    // the config field_labels; values are plain strings/rendered ADF text. Best-effort.
    Task<WorkItemFields> ReadWorkItemAsync(WorkItemRef item, IReadOnlyCollection<string> watchFields,
                                           CancellationToken ct = default);

    // Moves the item through a workflow transition by id (Jira: POST /issue/{key}/transitions).
    // Returns the resulting status name (best-effort; throws WorkTrackerTransitionException on hard failure).
    Task<string> TransitionAsync(WorkItemRef item, string transitionId, CancellationToken ct = default);
}

public sealed record WorkItemFields(string Key, string Url, IReadOnlyDictionary<string, string?> Fields);
```

## Jira implementation notes

- **Read**: `GET /rest/api/3/issue/{key}?fields=<watch_fields>`; flatten `fields.*`, render ADF description/AC to
  text, resolve `customfield_*` via existing display-name map. Returns `WorkItemFields` for `{{ticket_fields}}`.
- **Transition**: `GET /rest/api/3/issue/{key}/transitions` to validate the id exists (health check), then
  `POST` `{ "transition": { "id": "<transitionId>" } }`. 204 = success; surface 400/404 with an actionable message.
- **Writes** continue via existing `SetFieldsAsync` (whitelist filtered by caller, D7) and `AppendCommentAsync`
  (ADF). No direct datastore writes (FR-021).
- **Dry-run**: when enabled, the executor does not call `TransitionAsync`/`SetFieldsAsync` — logs a would-do.

## Errors

`WorkTrackerTransitionException` (invalid/again id, permission), `JiraNotConfiguredException` (Jira not active).
Bounded retries at the executor per FR-030; on exhaustion → manual exit, no partial write.
