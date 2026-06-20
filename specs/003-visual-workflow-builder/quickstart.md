# Quickstart: Visual Workflow Builder — Validation Guide

**Feature**: 003-visual-workflow-builder | **Plan**: [plan.md](plan.md)

This guide describes how to validate the Visual Workflow Builder end-to-end after
implementation. It is not implementation code — it is a runnable scenario guide.

---

## Prerequisites

1. `DBAIAzure.Web` compiles and starts without errors: `dotnet run --project src/DBAIAzure.Web`
2. LLM connector is configured (Anthropic API key present via user secrets or `appsettings.Development.json`)
3. Browser navigates to `http://localhost:5xxx` and the existing Threads page loads

---

## Scenario 1 — First-time user builds a three-node workflow

**Goal**: Validates FR-01 through FR-04, User Story 1, Success Criterion 1.

**Steps**:
1. Navigate to `/workflow-builder` — the builder page loads with the node palette visible.
2. Confirm the palette shows at least 5 categories: AI Steps, Decisions & Routing, Data,
   Notifications, Human Steps.
3. Confirm an example workflow is pre-loaded on the canvas.
4. Clear the canvas (toolbar "Clear" button).
5. From "AI Steps," drag "Reason & Summarize" onto the canvas.
6. From "Human Steps," drag "Wait for Approval" onto the canvas.
7. From "Notifications," drag "Send Notification" onto the canvas.
8. Draw a connection: hover the output port of "Reason & Summarize," drag to the input port of
   "Wait for Approval." A labelled arrow appears.
9. Draw a connection: hover the "Approved" output port of "Wait for Approval," drag to the input
   port of "Send Notification."
10. Confirm all three nodes show no red badges.

**Expected outcome**: Three connected nodes, no validation errors, in under 5 minutes.

---

## Scenario 2 — Configure an agentic node

**Goal**: Validates FR-04, User Story 3.

**Steps**:
1. Double-click the "Reason & Summarize" node — the configuration panel opens inline.
2. Confirm the panel shows exactly three fields: Goal, Input label, Output label.
3. Type: Goal = "Summarize the incoming request in three bullet points. Flag any mention of a deadline."
4. Close the panel.
5. Confirm the node label on the canvas updates to a shortened version of the goal text.
6. Confirm no amber badge appears (all required fields are populated).

**Expected outcome**: Node label updated, no validation badge.

---

## Scenario 3 — Generate code via the Chat Assistant

**Goal**: Validates FR-05.1–FR-05.7, User Story 2, Success Criterion 3 (topology fidelity).

**Steps**:
1. Open the Chat panel (toolbar "Chat" button or sidebar toggle).
2. Confirm the assistant's opening message names all three nodes by label.
3. Type: "Generate code for this workflow. The notification should go to the Teams channel
   configured in settings. Only send it on weekdays."
4. Wait for the response (max 15 seconds per Success Criterion 5).
5. Confirm the response contains a code block and a plain-English summary.
6. Confirm the code block includes:
   - A `WorkflowEvents` class
   - A `KernelProcessStep` subclass for the "Reason & Summarize" agentic node
   - A `ProcessBuilder` factory class connecting all three nodes
7. Click "Save to project" — confirm a file-name prompt appears, accept the default.
8. Confirm "Saved" confirmation appears.

**Expected outcome**: Complete, compilable code saved within 2 seconds of clicking Save.

---

## Scenario 4 — Run the workflow from the builder

**Goal**: Validates FR-07, User Story 5, Success Criteria 7–8.

**Steps**:
1. Click the "Run" button in the toolbar.
2. Confirm the plain-language input form appears: "What scenario should I test?"
3. Type: "A customer request about a billing error for invoice #1234."
4. The assistant confirms: "I'll test with a billing inquiry containing invoice number 1234."
5. Click "Confirm & Run."
6. Confirm the "Reason & Summarize" node animates within 2 seconds.
7. Wait for it to complete — confirm an output badge appears beneath the node.
8. Confirm the "Wait for Approval" node animates next and the run status shows "Paused."
9. The Run Output panel shows the approval request.
10. Click "Approve" — confirm the "Send Notification" node animates and completes.
11. Run status shows "Completed."

**Expected outcome**: Full three-node execution with per-node output visible.

---

## Scenario 5 — Save, reload, and validate round-trip fidelity

**Goal**: Validates FR-06, User Story 4, Success Criterion 4.

**Steps**:
1. With the three-node workflow on the canvas, click "Save."
2. Name it "Billing Request Handler."
3. Confirm "Saved" confirmation appears.
4. Navigate away (e.g. to the Threads page).
5. Navigate to `/workflow-gallery` — confirm "Billing Request Handler" appears with a
   thumbnail, node count (3), and last-modified timestamp.
6. Click the workflow — confirm the canvas loads with all three nodes in their saved positions
   and the "Reason & Summarize" goal text intact.
7. Confirm the chat history from the previous session is visible.

**Expected outcome**: Exact round-trip — positions, labels, goals, and chat history preserved.

---

## Scenario 6 — LLM unavailability graceful degradation

**Goal**: Validates FR-05.9.

**Steps** (requires temporarily misconfiguring the Anthropic API key in user secrets):
1. Set `Anthropic:ApiKey` to an invalid value and restart the application.
2. Open the workflow builder.
3. Confirm the canvas, palette, and node configuration are fully operational.
4. Open the Chat panel — confirm it shows "The assistant is currently unavailable" and the
   Submit button is disabled.
5. Attempt to click "Run" — confirm the execution input form shows the same unavailability
   message and the Confirm & Run button is disabled.
6. Restore the correct API key and reload — confirm Chat and Run become operational without
   navigating away.

**Expected outcome**: Canvas remains fully usable; LLM features degrade gracefully.

---

## Scenario 7 — Timeout enforcement

**Goal**: Validates FR-06B.2, FR-07.7.

**Steps**:
1. Open Workflow Settings — set "Stop automatically after: 1 minute."
2. Build a workflow with a single "Reason & Summarize" node that has a goal designed to run
   for a long time ("Write a 10,000-word essay on the history of computing").
3. Click Run, enter a test scenario, confirm execution starts.
4. Wait 60 seconds — confirm the node is marked "Timed out," the Run Output panel shows
   "This workflow took longer than 1 minute — you can increase the timeout in Workflow Settings,"
   and the "Run" button is restored.

**Expected outcome**: Timeout fires at exactly the configured duration with plain-language message.

---

## Unit Test Coverage Checkpoints

After each implementation phase, run `dotnet test` and confirm these categories pass:

| Test Class | What it validates |
|-----------|-------------------|
| `SqliteWorkflowRepositoryTests` | Owner isolation, name uniqueness, upsert round-trip |
| `WorkflowRuntimeBuilderTests` | ProcessBuilder graph from WorkflowDefinition; event routing |
| `WorkflowCodeGeneratorTests` | Topology serialization in prompt; diff accuracy; LlmUnavailableException |
| `WorkflowExecutionOrchestratorTests` | Timeout, RequestStop, SubmitApproval(false), RunUpdated frequency |
| `WorkflowDesignSkillServiceTests` | Question generation from topology; answer persistence |
| `LlmAvailabilityMonitorTests` | StateChanged fires on failure; auto-restore on recovery |
