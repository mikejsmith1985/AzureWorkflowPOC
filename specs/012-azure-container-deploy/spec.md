# Feature Specification: One-URL Azure Container Demo Deployment

**Feature Branch**: `feature/012-azure-container-deploy`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "I would like you to implement a feature that will allow us to deploy this to a container in azure exactly like the langgraph version: 'C:\ProjectsWin\DBAI' and I mean exactly the same. I want you to use the vault credentials and safely pass them so that I can give the URL to someone and they can just open it, the container will spin up (and 2 users can be inside it watching testing happen simultaneously without breaking anything). When it's static for whatever period of time — 10-15 mins, whatever it's set up as in the reference app — mirror it. The only thing the user should have to supply is their LLM API key, but they should be able to repoint any connector/integration they want."

---

## Clarifications

### Session 2026-06-25

- Q: Should demo data (run history, saved workflows, configured connectors) survive an idle scale-to-zero restart, or reset to a clean slate like the reference? → A: **Reset to deploy-time defaults (exact reference parity).** On each cold start, run history clears, saved workflows revert to the seeded set, the visitor's entered LLM key is gone, and any connector repointing reverts to the vault-seeded defaults. Every wake is a fresh demo.
- Q: Who is allowed to open the public URL? → A: **Fully public, no login (exact reference parity).** Anyone with the link opens it with no authentication; security rests on the link being unguessable and the seeded credentials being throwaway/dev instances.
- Q: When two people use the deployment at the same time, do they share one workspace (watch the same activity) or get isolated sandboxes? → A: **Shared workspace (exact reference parity).** Both visitors see the same connectors/config and watch the same runs live; if both change a connector, last-writer-wins.

> The reference application (`C:\ProjectsWin\DBAI`, the LangGraph version) is the canonical
> behavioural target for this feature. Where this spec says "mirror the reference," it means the
> observable behaviour an end user experiences must match: a single public URL that wakes a
> scaled-to-zero container on first hit, pre-seeded back-office connector credentials, a
> user-supplied LLM key as the only thing a visitor must enter, the ability to repoint any
> connector, and an automatic return to zero cost after a period of inactivity.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Hand someone a URL and they run a live test in minutes (Priority: P1)

A stakeholder wants to show the workflow product to a colleague or customer. They send one link.
The recipient opens it in a browser with no install, no login hurdle, and no credentials to chase
down — the back-office systems the demo talks to (ticketing, work-item tracking, messaging) are
already wired up. The only thing the recipient provides is their own LLM API key. Within a couple
of minutes they have a working environment and can run a real ticket through the pipeline and watch
it execute.

**Why this priority**: This is the entire point of the request — frictionless sharing. If a
recipient can't open a link and be productive with only their LLM key, the feature has failed,
regardless of any other capability.

**Independent Test**: From a machine that has never touched the project, open the shared URL, enter
only an LLM API key, start a demo run, and observe it execute end-to-end against the pre-configured
connectors.

**Acceptance Scenarios**:

1. **Given** a freshly shared URL, **When** a first-time visitor opens it in a standard browser,
   **Then** the application loads and presents a working environment with no software install and
   no need to supply anything other than an LLM API key.
2. **Given** the environment has loaded, **When** the visitor enters their LLM API key in the
   provided field, **Then** the key is accepted and used for subsequent runs without the visitor
   editing any files or environment settings.
3. **Given** an LLM key has been supplied, **When** the visitor starts a demo workflow/ticket run,
   **Then** the run executes against the pre-seeded connectors (ticketing, work-items, messaging)
   and the visitor sees live progress, exactly as in the reference application.
4. **Given** the visitor has not supplied an LLM key, **When** they attempt a run that needs the
   LLM, **Then** they are clearly prompted to provide their key rather than seeing an opaque
   failure.

---

### User Story 2 — Two people use it at once without stepping on each other (Priority: P1)

Two people open the same URL at the same time — for example a presenter driving and a customer
observing, or two evaluators exploring in parallel. They can both be "inside" the running container
and watch testing happen simultaneously. Neither person's activity corrupts the other's view or
crashes the shared environment.

**Why this priority**: The user explicitly requires that "2 users can be inside it watching testing
happen simultaneously without breaking anything." A demo that falls over when a second person joins
is not shippable.

**Independent Test**: Open the URL in two separate browsers/sessions at the same time, have both
trigger and/or observe activity concurrently, and confirm neither session errors, hangs, or
displays corrupted state because of the other.

**Acceptance Scenarios**:

1. **Given** the environment is open in one session, **When** a second person opens the same URL,
   **Then** both sessions are usable at the same time without the second arrival disrupting the
   first.
2. **Given** two simultaneous sessions, **When** activity occurs (a run is started, progresses, or
   completes), **Then** live updates are delivered to the appropriate session(s) without crashing,
   freezing, or showing corrupted state in either.
3. **Given** two simultaneous sessions, **When** both interact at the same time, **Then** the shared
   environment remains stable and responsive for the duration of a normal demo.

---

### User Story 3 — Repoint any connector to your own systems (Priority: P2)

The deployment arrives pre-wired with working back-office credentials so it runs out of the box, but
a visitor can override any connector/integration to point at their own environment — their own
ticketing instance, their own work-item project, their own messaging target — directly from the
running app, without redeploying.

**Why this priority**: The user wants recipients to be able to "repoint any connector/integration
they want." It is essential for a meaningful evaluation but secondary to simply getting the demo
open and running (US1/US2).

**Independent Test**: In the running app, change a connector's target/credentials to a different
environment, run an action that uses that connector, and confirm the action hits the newly
configured target rather than the pre-seeded default.

**Acceptance Scenarios**:

1. **Given** the app is running with pre-seeded connector defaults, **When** a visitor opens
   connector settings and changes a connector's target/credentials, **Then** subsequent actions for
   that connector use the new configuration without an app restart or redeploy.
2. **Given** a connector has been repointed, **When** the visitor runs a health/test check for that
   connector, **Then** the result reflects the newly configured target.
3. **Given** any single connector has been repointed, **When** other connectors are left untouched,
   **Then** the untouched connectors continue to use their pre-seeded defaults.

---

### User Story 4 — Idle to zero, wake on demand (Priority: P2)

When nobody has used the URL for a period of inactivity (matching the reference app's window —
roughly 10–15 minutes / the platform's default), the environment scales down so it costs nothing
while idle. The next time someone opens the URL, it wakes automatically; after a short startup wait
the app is ready again.

**Why this priority**: The user explicitly asked to mirror the reference's idle behaviour ("when it's
static for whatever period... mirror it"). It controls cost and matches the reference exactly, but
it is a non-functional/operational concern rather than the core sharing flow.

**Independent Test**: Leave the URL untouched until the environment scales down, confirm no compute
is running, then open the URL again and confirm it wakes and becomes usable after a brief startup.

**Acceptance Scenarios**:

1. **Given** no activity for the configured idle window, **When** the idle period elapses, **Then**
   the environment scales down so that it consumes no running compute.
2. **Given** the environment has scaled down, **When** a visitor opens the URL, **Then** it wakes
   automatically and becomes usable after a short, acceptable startup delay (no manual intervention).
3. **Given** the environment is waking from idle, **When** the visitor waits for startup, **Then**
   they are not shown a broken or error page — at worst a brief "starting up" experience consistent
   with the reference app.

---

### User Story 5 — Pre-seeded credentials delivered safely from the vault (Priority: P2)

The back-office credentials that make the demo work out of the box (ticketing, work-items,
messaging, and any other pre-configured integration) are sourced from the Forge Vault at deployment
time and delivered into the running environment securely. They are never printed into the
conversation, committed to the repository, baked into a shared image layer in plaintext, or exposed
back to visitors through the UI.

**Why this priority**: The user said to "use the vault credentials and safely pass them." This is a
security-correctness requirement that underpins US1 (pre-wired connectors) and aligns with the
project's zero-knowledge secrets rule.

**Independent Test**: Deploy using vault-sourced credentials, confirm the connectors work in the
running app, and confirm that no secret value appears in the repository, in deployment logs, in the
conversation, or in any UI response that echoes a stored secret back.

**Acceptance Scenarios**:

1. **Given** a deployment is performed, **When** credentials are needed for the pre-seeded
   connectors, **Then** they are obtained from the Forge Vault and injected into the environment
   without any secret value being written to source control or printed in logs/conversation.
2. **Given** the running app, **When** a visitor views connector settings, **Then** stored secret
   values are masked/withheld (the visitor can replace them but cannot read the seeded secrets back).
3. **Given** a deployment, **When** the LLM key is considered, **Then** it is *not* pre-seeded — the
   LLM key remains the one thing each visitor supplies themselves, matching the reference.

---

### Edge Cases

- **Cold-start collision**: Two visitors open the URL at nearly the same instant while the
  environment is waking from zero — both must end up in a working session without one of them
  receiving an error page.
- **Invalid / missing LLM key**: A visitor supplies a malformed or empty LLM key, or none at all,
  then triggers an LLM-dependent action — the app must surface a clear, recoverable prompt.
- **Repoint to an unreachable target**: A visitor repoints a connector to an environment that is
  down or wrong — the connector's health check must fail gracefully with a clear message, not crash
  the shared environment for the other user.
- **State reset on idle**: A visitor returning after an idle scale-down finds demo state reset to
  deploy-time defaults (their previously entered LLM key and any repointed connectors are gone),
  consistent with the reference — the app must make this fresh-start state usable, not broken.
- **Concurrent configuration change**: In the shared workspace, one visitor repoints a connector
  while another is mid-run — last-writer-wins applies to configuration, and the change must not
  corrupt the other visitor's in-flight run.
- **Simultaneous runs**: Two runs are started close together — their live progress streams must not
  cross-contaminate (each observer sees the correct run's events).

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST be deployable to a cloud-hosted container reachable at a single public
  URL that can be shared as-is, mirroring the reference application's deployment model.
- **FR-002**: A first-time visitor MUST be able to use the shared URL in a standard browser with no
  local installation and without supplying anything other than an LLM API key.
- **FR-003**: The system MUST provide an in-app way for a visitor to enter their own LLM API key,
  and MUST use that key for that visitor's LLM-backed runs.
- **FR-004**: The system MUST NOT require the LLM API key to be pre-seeded at deployment; the LLM key
  is the one credential the visitor supplies themselves.
- **FR-005**: The system MUST arrive with its back-office connectors (at minimum: ticketing intake,
  work-item tracking, and messaging — i.e. the connectors the reference seeds) pre-configured so the
  demo works end-to-end without the visitor configuring them.
- **FR-006**: Pre-seeded connector credentials MUST be sourced from the Forge Vault at deployment
  time and injected into the running environment without any secret value entering source control,
  logs, or the conversation.
- **FR-007**: The system MUST allow a visitor to repoint any connector/integration to a different
  target/credentials from within the running app, taking effect without a redeploy.
- **FR-008**: When a connector is repointed, the system MUST continue to use the pre-seeded defaults
  for all connectors that were not changed.
- **FR-009**: The system MUST mask or withhold stored secret values in the UI so a visitor cannot
  read pre-seeded secrets back, while still being able to overwrite them.
- **FR-010**: The system MUST support at least two simultaneous visitor sessions using the same URL
  without one session's activity crashing, freezing, or corrupting the other's experience.
- **FR-011**: The system MUST deliver live run/test progress to the appropriate active session(s) in
  real time during concurrent use.
- **FR-012**: After a configured period of inactivity (mirroring the reference app's idle window),
  the system MUST scale down so that it consumes no running compute while idle.
- **FR-013**: The system MUST wake automatically on the next request to the URL after an idle
  scale-down and become usable after a short startup delay, with no manual intervention and without
  presenting a broken page.
- **FR-014**: The deployment MUST be reproducible from committed configuration (the working tree
  contains everything needed to redeploy), with no secret values committed.
- **FR-015**: The system MUST surface clear, recoverable messaging for the common failure cases — a
  missing/invalid LLM key, and a repointed connector whose target is unreachable — without taking
  down the shared environment for other users.
- **FR-016**: Demo state MUST be ephemeral across an idle scale-down: on cold start the environment
  MUST reset to deploy-time defaults — run history cleared, saved workflows reverted to the seeded
  set, the visitor's entered LLM key gone, and all connector repointing reverted to the vault-seeded
  defaults — matching the reference application. The system MUST NOT require a persistent database to
  preserve state across idle restarts.
- **FR-017**: The shared URL MUST be openable by anyone who has the link with no authentication step
  (fully public, mirroring the reference). The system MUST NOT impose a login, passcode, or identity
  sign-in to reach the running app.
- **FR-018**: Simultaneous visitors MUST operate in a single shared workspace: they share the same
  connector/configuration state and can watch the same runs execute live. Concurrent configuration
  changes resolve last-writer-wins, and concurrent use MUST NOT crash or corrupt the shared
  environment (see FR-010/FR-011).

### Key Entities

- **Shared Deployment**: The single, publicly reachable running environment behind the shared URL.
  Has a lifecycle (idle → waking → running → idle), a set of pre-seeded connectors, and an idle
  window after which it scales to zero.
- **Visitor Session**: One person's interaction with the deployment via the URL. Carries (at least)
  the visitor's supplied LLM key for the duration defined by the isolation model, and receives live
  progress for runs it is observing.
- **Connector Configuration**: The target + credentials for an external integration (ticketing,
  work-items, messaging, LLM, etc.). Starts from a vault-sourced pre-seeded default and may be
  repointed at runtime. Secret portions are stored such that they are never readable back through
  the UI.
- **Seeded Secret Set**: The bundle of back-office credentials drawn from the Forge Vault at deploy
  time, injected into the environment, and never exposed in plaintext to source, logs, or visitors.
- **Run / Test Activity**: A workflow/ticket execution whose live progress is streamed to observing
  sessions; the unit two visitors "watch happening simultaneously."

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person who has never seen the project can, given only the URL, reach a working
  environment and complete a live run supplying nothing but their LLM API key, in under 5 minutes.
- **SC-002**: Two people open the same URL at the same time and both complete an observation of a
  live run with zero crashes, hangs, or corrupted-state incidents attributable to the other.
- **SC-003**: A visitor repoints at least one connector to a different target and a subsequent action
  demonstrably uses the new target, with all untouched connectors still using their defaults — with
  no redeploy.
- **SC-004**: After the configured idle window with no traffic, the environment is consuming no
  running compute; opening the URL again returns a usable app within an acceptable startup time
  comparable to the reference app.
- **SC-005**: An audit of the repository, deployment logs, and UI responses finds zero plaintext
  back-office secret values; connectors nonetheless function, proving secrets were delivered from the
  vault.
- **SC-006**: The LLM API key is never pre-seeded — every fresh visitor session must supply it,
  matching the reference behaviour.
- **SC-007**: The deployment can be reproduced from the committed configuration by following the
  documented steps, producing an equivalent working URL.

---

## Assumptions

- The reference LangGraph application at `C:\ProjectsWin\DBAI` is the authoritative behavioural
  model; its observable deployment behaviour (single public URL, scale-to-zero with ~10–15 min idle,
  vault-seeded connectors, user-supplied LLM key, runtime connector repointing) is what "exactly the
  same" means here. Implementation technology differs (this is a .NET / Semantic Kernel app, not
  Python/LangGraph) and need not match internally.
- "Connectors/integrations" in scope are those the current application already supports and the
  reference seeds — at minimum ticketing intake, work-item tracking, and messaging — plus the LLM.
  No new connector types are introduced by this feature.
- The idle window and wake-on-request behaviour follow the hosting platform's defaults as configured
  in the reference app, rather than a bespoke timer this feature invents.
- Cost posture matches the reference: minimal idle cost via scale-to-zero, single small running
  instance when active.
- The deployment is intended for demos/evaluations (as in the reference), not as a hardened
  multi-tenant production service.
- The Forge Vault contains the connector credentials needed to seed the demo (consistent with the
  reference app's existing seeded credential set), available to whoever performs the deployment.

## Dependencies

- Access to the Forge Vault for the pre-seeded connector credentials at deployment time.
- An Azure subscription / hosting target capable of the reference's deployment model (a public-URL
  container service that supports scale-to-zero and wake-on-request).
- The existing application's runtime connector-configuration capability (the in-app settings that
  let connectors be configured/repointed without a restart).

## Out of Scope

- Per-visitor user accounts, role-based permissions, or true multi-tenant data isolation beyond what
  the chosen concurrency model (Question 3) requires.
- Adding new connector/integration types not already supported by the application.
- Production-grade hardening (WAF, rate limiting, SLA guarantees, DR) beyond mirroring the
  reference's demo posture.
- Changing the application's core workflow/pipeline behaviour; this feature is about how it is
  packaged, shared, and run, not what it does once running.
