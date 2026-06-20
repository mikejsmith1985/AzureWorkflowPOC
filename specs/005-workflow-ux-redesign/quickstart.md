# Quickstart Validation Guide: Workflow Builder UX Master Review

**Feature**: `005-workflow-ux-redesign` | **Date**: 2026-06-20

This guide provides manual validation scenarios to confirm each UX improvement works
end-to-end after implementation. Run these in a local development environment with the
Blazor Server app running and an empty SQLite database.

---

## Prerequisites

```powershell
# Ensure app is running
cd C:\ProjectsWin\AzureWorkflowPOC
dotnet run --project src/DBAIAzure.Web

# Open browser
Start-Process "http://localhost:5000"
```

Delete all saved workflows via the gallery before running the first-run scenarios.

---

## Scenario 1 — First-Run Entry Choice (FR-01)

**Pre-condition**: Zero saved workflows in the database.

1. Navigate to `http://localhost:5000/workflow-builder`
2. **Expect**: Entry choice screen is visible with two options: "Start from scratch" and
   "Try the example." The canvas is not visible behind it.
3. Click "Try the example"
4. **Expect**: Canvas loads with a Trigger node (emerald green, "Start here" label), two
   downstream nodes, all connected. No amber "!" badges. Run button is green and enabled.
5. Navigate back to `http://localhost:5000/workflow-builder` again
6. **Expect**: Entry choice screen does NOT appear again (you now have a workflow).
   An empty canvas opens directly.

---

## Scenario 2 — Empty Canvas Welcome Overlay (FR-02)

**Pre-condition**: At least one workflow saved (so entry screen is skipped).

1. Navigate to `http://localhost:5000/workflow-builder` (opens empty canvas)
2. **Expect**: A large centred overlay shows "Drag a step from the left panel onto the
   canvas to begin" and the Triggers category in the palette has a pulsing glow.
3. Click "Start / Trigger" in the palette (click-to-place)
4. **Expect**: The welcome overlay disappears immediately. The Trigger node appears on
   the canvas. The pulsing palette highlight is gone.
5. Delete the Trigger node (right-click → Delete, or select + Delete key)
6. **Expect**: The full welcome overlay does NOT reappear. A minimal "canvas is empty —
   drag a step to continue" text label appears instead.

---

## Scenario 3 — Node Configuration Discoverability (FR-03)

1. Place a "Reason & Decide" node on the canvas (do not double-click it)
2. **Expect**: The node shows the amber "!" badge AND a small "Set up →" label visible
   without hovering.
3. Single-click the node to select it
4. **Expect**: A brief callout tooltip appears saying "Double-click to configure this step."
   It disappears after 2 seconds.
5. Double-click the node
6. **Expect**: The config panel opens with the Goal textarea already focused.
7. Type "Summarise the request in three bullet points"
8. **Expect**: The node label on the canvas updates live to reflect the typed text within
   200 ms. The "Set up →" label disappears.
9. Click "Done"
10. **Expect**: The panel closes. The amber "!" badge is gone. The node label matches what
    was typed. No separate save dialog appears.

---

## Scenario 4 — Run Button Disabled Reason (FR-04)

1. Open an empty canvas (no nodes)
2. **Expect**: Run button is grey. Adjacent text reads "Needs a trigger to start" — visible
   without hovering.
3. Place a "Reason & Decide" node (no trigger)
4. **Expect**: Text still reads "Needs a trigger to start"
5. Place a "Start / Trigger" node and configure it
6. **Expect**: Text changes to "Set up all steps first" (because the AI node is not yet configured)
7. Configure the AI node (double-click → type goal → Done)
8. **Expect**: The explanatory text disappears. The Run button fades from grey to green over
   ~300 ms.

---

## Scenario 5 — Inline Workflow Name Editing (FR-05)

1. Open a new empty workflow
2. **Expect**: The toolbar name field shows "Untitled Workflow" in an amber-highlighted editable
   state (not a passive label). Browser tab title shows "Workflow Builder — DBAIAzure."
3. Click the name field
4. **Expect**: An inline text input appears with "Untitled Workflow" fully selected.
5. Type "Billing Escalation Handler" and press Enter
6. **Expect**: The input reverts to a styled label showing "Billing Escalation Handler."
   Browser tab title updates to "Billing Escalation Handler — DBAIAzure" immediately.
7. Click the name and clear it entirely, then click elsewhere
8. **Expect**: Name reverts to its previous value. A brief tooltip "A workflow name is required"
   appears for ~1 second.

---

## Scenario 6 — Unsaved Changes Navigation Guard (FR-06)

1. Open any workflow and add a node (do not save)
2. Click the browser back button or navigate to `/workflow-gallery` via the nav
3. **Expect**: A confirmation modal appears with "You have unsaved changes." and three buttons:
   **"Save & Continue"**, **"Discard Changes"**, and **"Cancel — keep editing"**.
4. Click "Cancel — keep editing"
5. **Expect**: Navigation is cancelled; the builder is still open with the unsaved change intact.
6. Repeat step 2, then click "Save & Continue"
7. **Expect**: The workflow is saved and navigation to the gallery proceeds.
8. Repeat with a fresh change, then click "Discard Changes"
9. **Expect**: Navigation proceeds without saving. Changes are lost.
10. Open the same workflow again and make no changes, then navigate away
11. **Expect**: No confirmation appears (guard does not fire when nothing has changed).

---

## Scenario 7 — Chat Change Indicator (FR-07)

1. Open a workflow with at least two connected nodes
2. Open the chat panel and generate code
3. Close the chat panel
4. **Expect**: No orange dot on the Chat button (code is current with the canvas)
5. Add a new node to the canvas
6. **Expect**: An orange dot appears on the Chat button within 500 ms
7. Click the Chat button to open the panel
8. **Expect**: The orange dot disappears. The assistant has a new message at the bottom:
   "Your workflow has changed since I last generated code. Want me to update it?" with an
   "Update code" button.
9. Click "Update code"
10. **Expect**: Regeneration runs and the response renders as a compact diff (green +lines,
    red -lines, grey context). A "Show full code" link is visible below the diff.

---

## Scenario 8 — Post-Run Feedback Pre-Population (FR-08)

1. Run a workflow (it can fail — failure state is equally testable)
2. After the run completes, observe the output badge on one node
3. Click "Did this do what you expected?" on that badge
4. **Expect**: The chat panel opens. The message input is pre-populated with text including
   the node's name, its status (succeeded/failed), a snippet of its goal, and a snippet of
   its output. The template ends with "Did this do what you expected? If not, describe what
   you wanted instead."
5. Click send without editing the pre-populated text
6. **Expect**: The assistant responds with a concrete suggestion for improving the node's
   Goal field.

---

## Scenario 9 — Gallery Improvements (FR-09)

1. Save at least one workflow and navigate to `/workflow-gallery`
2. **Expect**: Each card shows a coloured SVG thumbnail in the preview area (not "No preview").
   The thumbnail shows coloured rectangles connected by lines.
3. **Expect**: A search input is visible at the top of the gallery page (always visible,
   not conditional on count).
4. Type part of a workflow name into the search box
5. **Expect**: Cards filter in real time (within 150 ms) to show only matching workflows.
6. Clear the search
7. **Expect**: All cards are shown again. The search input remains visible.
8. Type a search term that matches nothing
9. **Expect**: "No workflows match '[term]'" message with a "Clear search" button.
10. Check the footer of any gallery card
11. **Expect**: Node type summary is shown (e.g., "1 trigger, 1 AI step, 1 approval") instead
    of "4 step(s)".

---

## Scenario 10 — Keyboard Shortcuts Panel (FR-10)

1. Open the workflow builder
2. Observe the toolbar far-right area
3. **Expect**: A "?" icon button is visible after the Run/Stop button.
4. Click the "?" button
5. **Expect**: A floating panel appears listing all keyboard shortcuts:
   - Undo last action: Ctrl+Z
   - Redo last action: Ctrl+Y
   - Delete selected: Delete / Backspace
   - Save: Ctrl+S
6. Press Escape
7. **Expect**: The panel closes and focus returns to the canvas.
8. Click outside the panel (if re-opened)
9. **Expect**: The panel closes.

---

## Automated Test Coverage

The following test files validate these scenarios programmatically:

| Test file | Covers |
|-----------|--------|
| `WorkflowThumbnailGeneratorTests.cs` | SVG output correctness, null on empty workflow |
| `WorkflowCodeDiffServiceTests.cs` | Added/removed/context lines, identical inputs |
| `WorkflowEntryChoiceModalTests.cs` | Shown on zero workflows, hidden when count > 0 |
| `WorkflowUnsavedChangesModalTests.cs` | Guard fires, Stay cancels, Leave navigates |
| `WorkflowToolbarNameEditTests.cs` | Span→input toggle, Enter commits, blank reverts |
| `WorkflowNodeRendererAffordanceTests.cs` | "Set up" label visible on unconfigured node |
