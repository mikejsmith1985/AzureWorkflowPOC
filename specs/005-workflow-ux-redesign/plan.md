# Implementation Plan: Workflow Builder UX Master Review

**Branch**: `feature/visual-workflow-builder` | **Date**: 2026-06-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-workflow-ux-redesign/spec.md`

---

## Summary

This plan closes 20 identified UX gaps across the Visual Workflow Builder without redesigning
the canvas model, changing the database schema, or introducing new projects. Work is pure
Blazor/Razor component changes in `DBAIAzure.Web` and two new service interfaces in
`DBAIAzure.Core`.

The improvements fall into three cohorts:

1. **First-run clarity** (FR-01 – FR-02): Entry choice screen for zero-workflow users; empty
   canvas welcome overlay with palette call-to-action.

2. **Interaction discoverability** (FR-03 – FR-05, FR-10): On-node "Set up" affordance;
   single-click tooltip; live Goal-to-label sync; always-visible Run disabled reason; inline
   workflow name editing; keyboard shortcuts panel.

3. **Feedback completeness** (FR-06 – FR-09): Unsaved-changes navigation guard implementation;
   orange chat-change dot; post-run feedback pre-population; gallery thumbnails and search.

One new NuGet dependency is introduced (`DiffPlex`) for compact diff rendering. Two new service
interfaces and three new Blazor components are added. All other changes modify existing files.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8 (Blazor Server)

**Primary Dependencies**:
- Z.Blazor.Diagrams v3.0.4.1 — canvas, node/link models; `SelectionChanged` event used for
  single-click tooltip
- Microsoft.SemanticKernel v1.77.0 — AI chat (no changes in this sprint)
- **DiffPlex v1.7.2** (new) — line-level diff computation for compact code diff rendering
- Tailwind CSS (CDN, utility-first) — component styling

**Storage**: SQLite via EF Core 8. `WorkflowDefinition.ThumbnailSvg` (`string? ThumbnailSvg`)
already exists on the domain model and the storage entity — no migration required. Thumbnails
are generated at save time and written to the existing column.

**Testing**: xUnit 2.9.0 + bUnit 1.37.7 (Blazor component testing)

**Target Platform**: ASP.NET Core 8, Blazor Server, browser-rendered via SignalR

**Performance Goals**:
- Entry screen to canvas: < 300 ms additional latency (one extra `ListByOwnerAsync` call,
  expected sub-50 ms on SQLite with 0–100 rows)
- Thumbnail generation: < 50 ms for up to 20 nodes (pure C# string builder, no I/O)
- Inline name commit: < 16 ms (synchronous Blazor state flip, no network call)
- Chat change dot: appears within 500 ms of topology change (matches FR-07.1)
- Gallery search: < 150 ms (client-side LINQ filter over already-loaded list)

**Constraints**:
- Navigation guard must use `context.PreventNavigation()` + `_pendingNavigationUri` field
  to support the custom "Leave" action — no JS `window.confirm`, no JS interop
- Thumbnail SVG generated from `WorkflowDefinition.Nodes` domain data (not the rendered DOM)
  so generation is server-side, deterministic, and requires no JS round-trips
- `DiffPlex` added to `DBAIAzure.Core` only — preserves the shared-library boundary
- The `WorkflowChatPanel` already has `_showWorkflowChangedBanner` and `NotifyWorkflowChanged()`;
  the new orange dot on the toolbar is a parent-managed parameter, not a second channel

**Scale/Scope**: Single-user Blazor Server session; gallery up to ~100 workflows in MVP scope

---

## Constitution Check

| Article | Gate | Status |
|---------|------|--------|
| I — Prime Directive | Best route chosen; no DOM hacks; server-capable work stays server-side | ✓ PASS |
| II — Process Protection | No wildcard kills needed | ✓ N/A |
| III — Branching | Continues on `feature/visual-workflow-builder` | ✓ PASS |
| IV — Code Quality | PascalCase types, `_camelCase` fields, XML docs on all new public members; no magic strings | ✓ PASS |
| V — Testing (3-layer) | Unit tests (`ThumbnailGenerator`, `DiffService`); bUnit tests (entry modal, unsaved guard, name edit, node affordance); no I/O in unit layer | ✓ PASS |
| VI — Docs Discipline | CHANGELOG.md updated in PR; no auxiliary summary docs | ✓ PASS |
| VII — Framework-First | See analysis below | ✓ PASS with documented gaps |
| VIII — Release | Not a release sprint | ✓ N/A |
| IX — Secrets | No secrets touched | ✓ N/A |
| X — Verification | Passing xUnit/bUnit tests + browser observation for each UX behaviour | ✓ PASS |
| XI — Output Restraint | Plan artifacts in `specs/005/`; no ad-hoc status docs | ✓ PASS |

### Article VII — Framework-First Analysis

**Blazor Server + Z.Blazor.Diagrams provide natively — use these**:
- `NavigationManager.RegisterLocationChangingHandler` + `LocationChangingContext.PreventNavigation()`
  → unsaved-changes guard (FR-06). No JS `window.confirm` needed.
- `ElementReference` + `@onblur` / `@onkeydown` on `<input>` elements
  → inline workflow name editing (FR-05). No JS interop for focus management.
- Z.Blazor.Diagrams `SelectionChanged` event
  → detect single-click selection for the "Double-click to configure" tooltip (FR-03.2).
- CSS `transition` property
  → Run button grey→green animation (FR-04.2). No JS animation library.
- Standard Blazor parameter/callback pattern
  → orange chat-change dot via `HasCanvasChangedSinceCodeGen` bool on `WorkflowToolbar`.

**Documented gaps requiring custom code**:
- **SVG thumbnail generation**: No Blazor or Z.Blazor.Diagrams primitive produces a static
  SVG from domain model data. *Custom: `WorkflowThumbnailGenerator` service in `DBAIAzure.Core`
  reads `WorkflowDefinition.Nodes` positions/types and emits a deterministic SVG string.*
- **Compact code diff rendering**: Blazor has no line-diff primitive. *Custom: `DiffPlex`
  NuGet (MIT licence, 10 M+ downloads) via `WorkflowCodeDiffService` in `DBAIAzure.Core`.*
- **Entry choice modal + empty canvas welcome overlay**: No framework equivalent.
  *Custom: `WorkflowEntryChoiceModal.razor` + inline welcome overlay in `WorkflowCanvas.razor`.*
- **Unsaved-changes confirmation modal**: Blazor Server has no built-in confirmation for the
  navigation guard callback. *Custom: `WorkflowUnsavedChangesModal.razor` rendered conditionally
  when `_isUnsavedChangesModalOpen` is true; "Leave" calls `NavigationManager.NavigateTo(_pendingNavigationUri)`.*

---

## Project Structure

### Documentation (this feature)

```text
specs/005-workflow-ux-redesign/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/
│   ├── IWorkflowThumbnailGenerator.md
│   └── IWorkflowCodeDiffService.md
└── tasks.md             ← Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/DBAIAzure.Core/
├── Interfaces/
│   ├── IWorkflowThumbnailGenerator.cs    [NEW]  SVG schematic generation contract
│   └── IWorkflowCodeDiffService.cs       [NEW]  compact diff computation contract
└── Services/
    ├── WorkflowThumbnailGenerator.cs     [NEW]  pure C# SVG builder from node positions/types
    └── WorkflowCodeDiffService.cs        [NEW]  DiffPlex wrapper; returns DiffResult

src/DBAIAzure.Web/
├── Components/WorkflowBuilder/
│   ├── WorkflowEntryChoiceModal.razor    [NEW]  "Start from scratch / Try the example" screen
│   ├── WorkflowUnsavedChangesModal.razor [NEW]  navigation guard confirmation dialog
│   ├── WorkflowKeyboardShortcutsPanel.razor [NEW] floating shortcuts reference ("?" button)
│   ├── WorkflowBuilder.razor            [MODIFY] _hasUnsavedChanges, _savedWorkflowCount,
│   │                                             _pendingNavigationUri, _isUnsavedChangesModalOpen,
│   │                                             _hasCanvasChangedSinceCodeGen; entry screen
│   │                                             condition; unsaved guard; feedback pre-population
│   ├── WorkflowCanvas.razor             [MODIFY] empty-canvas welcome overlay; SelectionChanged
│   │                                             single-click tooltip; thumbnail trigger callback
│   ├── WorkflowNodeRenderer.razor       [MODIFY] "Set up" affordance label on unconfigured nodes
│   ├── WorkflowNodeConfigPanel.razor    [MODIFY] rename "Save" → "Done"; raise OnConfigCommitted
│   │                                             event after applying changes
│   ├── WorkflowToolbar.razor            [MODIFY] inline name edit (span↔input toggle);
│   │                                             always-visible Run disabled reason text;
│   │                                             HasCanvasChangedSinceCodeGen parameter for orange dot;
│   │                                             "?" button for keyboard shortcuts panel
│   ├── WorkflowChatPanel.razor          [MODIFY] compact diff via IWorkflowCodeDiffService;
│   │                                             pre-populate feedback message from node badge
│   ├── WorkflowGallery.razor            [MODIFY] always-visible search input; search filtering
│   └── WorkflowGalleryCard.razor        [MODIFY] node-type summary label (replaces step count)
└── wwwroot/css/
    └── workflow-canvas-animations.css   [MODIFY] welcome overlay fade; Run button transition;
                                                  diff line colour rules (.diff-add, .diff-remove)

tests/DBAIAzure.Tests/
├── WorkflowThumbnailGeneratorTests.cs   [NEW] unit — SVG output correctness, node colours, empty workflow
├── WorkflowCodeDiffServiceTests.cs      [NEW] unit — added/removed/context lines, identical files
├── WorkflowEntryChoiceModalTests.cs     [NEW] bUnit — shown on zero workflows, hidden when workflows exist
├── WorkflowUnsavedChangesModalTests.cs  [NEW] bUnit — guard fires on change, Leave navigates, Stay closes
├── WorkflowToolbarNameEditTests.cs      [NEW] bUnit — span→input toggle, Enter commits, blank reverts
└── WorkflowNodeRendererAffordanceTests.cs [NEW] bUnit — "Set up" label visible on unconfigured node
```

**Structure Decision**: Single Blazor Server project. All new components land in the existing
`Components/WorkflowBuilder/` folder. New interfaces and services in `DBAIAzure.Core`. No new
projects, no new namespaces beyond the existing `DBAIAzure.Web.Components.WorkflowBuilder` and
`DBAIAzure.Core.Services`.

---

## Complexity Tracking

No constitution violations. All four custom code items are documented gaps against Blazor Server
and Z.Blazor.Diagrams v3 with justifications in the Article VII section above.
