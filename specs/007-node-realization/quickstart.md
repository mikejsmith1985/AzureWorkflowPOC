# Quickstart & Validation: Node Realization

This guide proves the feature end-to-end. It does not contain implementation code — see
`data-model.md` and `contracts/` for shapes and `tasks.md` (after `/speckit-tasks`) for the work.

## Prerequisites

- .NET 8 SDK (user-local, resolved via `global.json`).
- A configured **LLM connector** (Anthropic key) — realization and agentic execution need it.
- At least one **messaging connector** (e.g., Teams) configured for the Notify-node path; and
  one connector intentionally **left unconfigured** to exercise the "blocked" path (US4).
- App launched via `scripts/start-web.ps1`; E2E via `scripts/run-e2e.ps1` (never by building the
  binary directly).

## Scenario A — Plain language → production-ready, hands-off (US1, SC-1/2/6)

1. In the builder, create a workflow with plain-language nodes only: Trigger → AgenticReason
   ("Summarise the customer's problem") → FunctionRoute ("Is it urgent?") → HumanApproval →
   FunctionNotify ("Email the customer"). Connect them. Do **not** open any config form.
2. Click **"Make it real."**
   - **Expect**: per-node progress is visible; within one session every node gets a reviewable,
     plain-language proposal (agent instruction + model + output shape; branch conditions; notify
     connector + message; approval prompt).
3. Accept all proposals (single confirmation).
   - **Expect**: every node shows a **Realized** badge; the workflow readiness indicator flips to
     **Ready to run**; the **Run** action becomes enabled.
4. Run the workflow with a sample input.
   - **Expect**: it executes through the existing run flow to completion using the realized config
     — no further configuration prompts. (SC-6 proof: observe the run reach a terminal state.)

## Scenario B — Review & adjust a proposal (US2, SC-3/7)

1. After Scenario A step 2, open the AgenticReason node's proposal.
   - **Expect**: the summary describes what the step does / uses / produces in plain language —
     no raw code or unexplained fields.
2. Edit the proposed instruction (e.g., cap the summary at 3 sentences) and Accept.
   - **Expect**: only that node updates; other accepted nodes are unchanged.
3. Save, navigate away, return.
   - **Expect**: the edited config persists (consistent with the persistence guarantees from the
     auto-save/resume fixes).

## Scenario C — Single-node realization (US3, SC-5)

1. On the realized workflow, add one FunctionData node ("Save to records system") and connect it.
2. Realize **only** that node.
   - **Expect**: the new node gets a proposal; **no** previously realized node changes; overall
     readiness recalculates (now not-ready until the new node is accepted/valid).

## Scenario D — Honest gating when a connector is missing (US4, SC-4)

1. Ensure the messaging connector the Notify node needs is **not** configured (or unhealthy).
2. Run "Make it real."
   - **Expect**: the Notify node is flagged **Blocked — needs setup** with a plain-language reason
     and a path to connector configuration; the workflow is **not** marked production-ready; the
     **Run** action stays disabled with the reason surfaced.
3. Configure the connector, re-evaluate.
   - **Expect**: the node clears to **Realized** and readiness re-checks.

## Scenario E — Out-of-date detection (SC-8)

1. On a realized node, change its plain-language label/goal (or rewire an edge).
   - **Expect**: the node flips to **Out of date** and offers one-click re-realization of just
     that node; the workflow is no longer production-ready until re-accepted.

## Automated validation (Article V / X)

- **Unit** (`dotnet test tests/DBAIAzure.Tests`): proposal generation per node type with a mocked
  `IStructuredCompletionService`; readiness rules (realized/blocked/out-of-date/needs-input);
  config↔`FunctionConfig` round-trip; `AcceptProposal` single-node isolation.
- **Integration**: a real structured-output call returns a schema-valid config; readiness gates a
  Notify node when its connector is absent/unhealthy via the real `IConnectorHealthChecker`.
- **E2E** (`scripts/run-e2e.ps1`, `NodeRealizationTests.cs`): Scenarios A and D end-to-end against
  the running app.

**Definition of done for this feature**: Scenarios A–E pass manually, the automated suites are
green, and a realized workflow has been observed running to completion (live evidence, not just a
green build).
