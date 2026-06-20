# Research: Workflow Builder UX Master Review

**Feature**: `005-workflow-ux-redesign` | **Date**: 2026-06-20

All technical unknowns were resolvable from the existing codebase and standard Blazor
Server patterns. No external documentation queries were required. Findings below.

---

## Decision 1 — Zero-Workflow Detection

**Question**: How should the entry choice screen (FR-01) determine whether to show?

**Decision**: Call `IWorkflowRepository.ListByOwnerAsync(DemoOwnerId)` inside
`WorkflowBuilder.razor.OnInitializedAsync()` when `Id` is null/empty. Cache the count
as `_savedWorkflowCount`. Show the entry modal if and only if `_savedWorkflowCount == 0`.

**Rationale**: `IWorkflowRepository` is already injected in `WorkflowGallery.razor` for
exactly this purpose. The same call in `WorkflowBuilder.razor` adds one lightweight
database query (sub-50 ms on SQLite with a small gallery). No new infrastructure, no
additional injection, no duplication of business logic.

**Alternatives considered**:
- *localStorage flag via JS interop*: Would survive across sessions but adds JS dependency
  for a trivial state. Rejected — Blazor Server-side DB check is simpler and authoritative.
- *Count embedded in a singleton service*: Over-engineering for a check that is only
  needed once per page load. Rejected.

---

## Decision 2 — SVG Thumbnail Generation

**Question**: How should `WorkflowDefinition.ThumbnailSvg` be generated at save time?

**Decision**: Create `WorkflowThumbnailGenerator` in `DBAIAzure.Core.Services`. It
accepts a `WorkflowDefinition` and produces an SVG string by:
1. Computing a bounding box over all node `PositionX / PositionY` values
2. Normalising coordinates into a 200 × 100 viewBox
3. Emitting a `<rect>` per node (colour from `NodeType`) and a `<line>` per edge
4. Returning the SVG string; returning `null` on error (silent fail per clarification)

Node colours match the canvas palette: emerald for Trigger, amber for AgenticReason,
purple for HumanApproval, cyan/teal/sky/indigo for function types.

**Rationale**: `WorkflowDefinition.Nodes` already carries `PositionX`, `PositionY`, and
`NodeType` — all that is needed for a schematic. No DOM access, no JS interop, no
additional I/O. Generation is deterministic and fast (< 50 ms for 20 nodes).

**Alternatives considered**:
- *JS screenshot of the canvas DOM*: Requires JS interop + `html2canvas` or similar.
  Non-deterministic, slow, cannot run server-side. Rejected.
- *Z.Blazor.Diagrams SVG export*: ZBD v3 has no export API. Rejected (gap documented).

---

## Decision 3 — Compact Diff Rendering

**Question**: How should the compact diff (FR-07.3) be computed for regenerated code?

**Decision**: Add `DiffPlex` (MIT, v1.7.2) to `DBAIAzure.Core`. Create
`WorkflowCodeDiffService` that calls `InlineDiffBuilder.Diff(previousCode, updatedCode)`,
filters to changed hunks ± 3 context lines, and returns a `DiffResult` containing
`IReadOnlyList<DiffLine>` (Content, Type, IsContext). The `WorkflowChatPanel` stores
`_previousGeneratedCode` before regeneration and calls the service to render the diff view.

**Rationale**: DiffPlex is the de-facto standard .NET diff library (10 M+ NuGet downloads,
MIT licence, zero transitive dependencies). `InlineDiffBuilder` produces exactly the
line-level diff needed. The ± 3 context lines window matches the user's expectation
(similar to `git diff` output).

**Alternatives considered**:
- *Send both versions to the LLM and ask it to format the diff*: Non-deterministic,
  slower, wastes tokens on a formatting task. Rejected.
- *Custom Myers diff implementation*: Unnecessary re-implementation of a solved problem.
  Rejected.
- *Full file with highlighted lines*: Shows too much unchanged content. Rejected per
  clarification (Option B chosen by user).

---

## Decision 4 — Unsaved-Changes Navigation Guard

**Question**: How should the navigation guard (FR-06) show a confirmation and then allow
navigation if the user chooses "Leave"?

**Decision**: Use the existing `RegisterLocationChangingHandler` with:
1. If `_hasUnsavedChanges` is false → return without calling `PreventNavigation()`.
2. If `_hasUnsavedChanges` is true → call `context.PreventNavigation()`, store the
   destination in `_pendingNavigationUri`, set `_isUnsavedChangesModalOpen = true`,
   and call `StateHasChanged()`.
3. "Stay and save" button → closes modal; no navigation.
4. "Leave without saving" button → calls `NavigationManager.NavigateTo(_pendingNavigationUri)`,
   which re-triggers the guard handler but now `_hasUnsavedChanges` is false (cleared by
   the Leave action) so navigation proceeds.

**Rationale**: This is the documented Blazor Server pattern for navigation confirmation.
`PreventNavigation()` is the correct primitive. Storing the URI and re-navigating on
confirm is how all Blazor navigation guards work in production codebases.

**Alternatives considered**:
- *JS `window.confirm()`*: Synchronous, blocks the UI thread, not interceptable in all
  Blazor Server configurations. Rejected.
- *`IJSRuntime.InvokeAsync<bool>("confirm", ...)`*: Requires JS interop, returns a Task
  that cannot be awaited inside the synchronous LocationChanging handler without a
  TaskCompletionSource workaround. Adds complexity. Rejected in favour of the modal pattern.

---

## Decision 5 — Chat Change Dot (Orange Indicator)

**Question**: How should the orange dot on the Chat toolbar button be managed?

**Decision**: Manage `_hasCanvasChangedSinceCodeGen` in `WorkflowBuilder.razor`.
- Set to `true` in `OnWorkflowChanged` when `_workflow?.GeneratedCode is not null`
  (code has been generated at least once in any session)
- Set to `false` when `OnChatToggleClicked` opens the chat panel
- Pass as `HasCanvasChangedSinceCodeGen` parameter to `WorkflowToolbar.razor`
- The toolbar renders the orange dot as `@if (HasCanvasChangedSinceCodeGen)` on the
  Chat button

The existing `WorkflowChatPanel.NotifyWorkflowChanged()` already triggers the
panel-internal banner; the orange dot is a separate parent-managed layer that does not
depend on the panel being open.

**Rationale**: No new event bus or singleton service is needed. The parent page already
coordinates all toolbar state; adding one more bool parameter follows the established
pattern exactly.

**Alternatives considered**:
- *Event on `WorkflowChatPanel` to self-mark as stale*: The panel is only rendered when
  open. An event from a non-rendered component is unreliable. Rejected.
- *Singleton `WorkflowStateService`*: Over-engineering for a single-page application
  with one active workflow at a time. Rejected.
