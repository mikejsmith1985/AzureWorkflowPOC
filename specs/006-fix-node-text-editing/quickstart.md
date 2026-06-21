# Quickstart Validation Guide: Fix Node Text Editing

**Date**: 2026-06-21 | **Plan**: [plan.md](plan.md)

---

## Prerequisites

- .NET 8 SDK available (resolved via `global.json`)
- App running: `dotnet run --project src/DBAIAzure.Web`
- Browser open at `http://localhost:5099`
- Playwright E2E runner available: `.\scripts\run-e2e.ps1`

---

## Scenario 1 — Config panel inputs no longer reset mid-edit (Bug Fix)

This verifies the `OnParametersSet` re-initialisation bug is resolved.

**Steps**:
1. Open the Workflow Builder and place an "AI Agent" (AgenticReason) node on the canvas.
2. Double-click the node to open the config panel.
3. Click into the Goal textarea. The field is empty or shows a previous value.
4. Type a long phrase slowly (10+ characters). Do NOT press Enter or click Done.
5. Wait 300 ms (longer than the 200 ms debounce).

**Expected**: The Goal field retains every character typed. The canvas label preview may
update (correct behaviour), but the textarea itself must not lose any typed text or
reset to an earlier value.

**Failure indicator**: Any character disappears or the field reverts to a prior value
while the user is still typing.

---

## Scenario 2 — Inline label edit via double-click (New Feature)

**Steps**:
1. Place any node (e.g., "AI Agent") on the canvas. Its label shows "AI Agent."
2. Double-click the node's name text ("AI Agent") in the coloured header area.
3. The text becomes an `<input>` field with the current label selected.
4. Clear the field (Ctrl+A + Delete).
5. Type "Triage Incoming Ticket".
6. Press Enter.

**Expected**:
- The node immediately shows "Triage Incoming Ticket" in the header.
- The config panel does NOT open (because the double-click was on the label region only).
- Clicking elsewhere on the canvas and back again: the label still reads "Triage Incoming
  Ticket" — not "AI Agent."

---

## Scenario 3 — Escape cancels without resetting to default

**Steps**:
1. With the node from Scenario 2 showing "Triage Incoming Ticket":
2. Double-click the label. Field shows "Triage Incoming Ticket."
3. Delete all text.
4. Press Escape.

**Expected**: The node returns to showing "Triage Incoming Ticket" — not "AI Agent" and
not a blank label. Escape restores the pre-edit value, not the type-default.

---

## Scenario 4 — Label undo via Ctrl+Z

**Steps**:
1. Start with a node labelled "Triage Incoming Ticket."
2. Double-click the label → type "Step A" → press Enter. Node shows "Step A."
3. Double-click the label → type "Step B" → press Enter. Node shows "Step B."
4. Press Ctrl+Z once.

**Expected**: Node reverts to "Step A."

5. Press Ctrl+Z again.

**Expected**: Node reverts to "Triage Incoming Ticket."

6. Press Ctrl+Z again.

**Expected**: No further label changes (Ctrl+Z now rolls back earlier canvas actions,
not the label history). Node stays on "Triage Incoming Ticket."

---

## Scenario 5 — Empty label shows placeholder, not blank

**Steps**:
1. Double-click any node's label.
2. Clear all text.
3. Press Enter.

**Expected**: The node header shows either the type-default label ("AI Agent") or a
visually distinct "Untitled node" placeholder in grey italic. The node never shows a
completely blank header.

4. Double-click the label again.
5. Confirm the input field is empty — the placeholder text ("Untitled node") is not
   pre-filled into the editable field.

---

## Scenario 6 — Keyboard-only editing (no mouse)

**Steps**:
1. Using only the keyboard, Tab until a canvas node receives focus (visible focus ring).
2. Press Enter to activate the label input.
3. Type "Keyboard Label."
4. Press Enter to commit.

**Expected**: The node shows "Keyboard Label." No mouse was used at any point.

---

## Scenario 7 — All node types editable

Repeat Scenario 2 for each node type in the palette:
- Start / Trigger
- AI Agent (AgenticReason)
- Ask a Person (HumanApproval)
- Smart Branch (FunctionRoute)
- Transform (FunctionTransform)
- Notify (FunctionNotify)
- Save / Load (FunctionData)

**Expected**: Every node type passes the same edit-commit-verify cycle. No node type
resists editing or resets to its default after commit.

---

## Scenario 8 — Existing interactions not regressed

After completing Scenarios 1–7, verify:
- Ports can still be dragged to create connections.
- Nodes can be dragged to new positions.
- Right-click context menu still appears on nodes.
- Node deletion (Delete key or context menu) still works.
- Ctrl+Z still undoes node deletion (not just label changes).
- The config panel still opens on double-click of the node BODY (outside the label span).

---

## Automated Validation

Run the full Playwright E2E suite:

```powershell
.\scripts\run-e2e.ps1
```

All existing tests must pass. New tests targeting the scenarios above are specified in
`tasks.md` and must be authored before the fix is considered shippable (Red → Green → Refactor).

Key new test file: `tests/DBAIAzure.E2ETests/WorkflowNodeLabelEditTests.cs`
