# Feature Specification: AI-First Console Assistant Grounded in the In-App User Guide

**Feature Branch**: `feature/015-ai-assistant-console`

**Created**: 2026-06-26

**Status**: Draft

**Input**: User direction (during clarification of the 014 redesign): "you should generate an in app
user guide which is what trains the AI on the application so it can quickly and accurately answer
anything the user needs or wants from it. We're building an AI first application so the assistant
should be able to do anything the user can do." Split out of `specs/014-admin-console-ui-redesign/`
into its own feature.

---

> **AI-first.** This product is built AI-first: the Assistant is a primary way to use the console, not
> a bolt-on. The Assistant can **answer anything about the application** (grounded in the in-app User
> Guide) and **perform anything the user can perform** in the console, through the same underlying
> capabilities the UI uses.

> **Builds on the redesign (014).** `specs/014-admin-console-ui-redesign/` delivers the Assistant
> **panel chrome** (a persistent, collapsible right-hand panel) and a **human-readable User Guide**
> section. This feature (015) makes that panel **intelligent and agentic**, and adopts that same User
> Guide as the Assistant's **single knowledge source**. 015 depends on 014; 014 does not depend on 015.

> **Acts through existing capabilities only.** The Assistant does not invent product features. It can
> do what the UI already allows; its breadth grows as the product's own capabilities grow. It never
> exceeds what the current user could do by hand, and it asks before doing anything consequential.

---

## Clarifications

### Session 2026-06-26

- Q: Is the Assistant a presentation shell or a real capability? → A: **A real, AI-first, agentic
  assistant.** It answers app questions and performs actions on the user's behalf.
- Q: What grounds the Assistant's answers? → A: **The in-app User Guide is its single knowledge
  source.** The guide a human reads and the knowledge the AI answers from are the same content, so
  they cannot diverge.
- Q: What can the Assistant *do*? → A: **Anything the user can do** in the console (configure a
  connector, create/edit/run a workflow, register/build/run an app, respond to a review item,
  navigate), executed through the same underlying capabilities so the result is identical to doing it
  by hand.
- Q: What guardrails apply? → A: **Confirm-before-consequential and permission-bounded.** Destructive,
  outward-facing, or spending/committing actions require explicit confirmation; the Assistant never
  performs or exposes anything the current user could not do or see directly.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Ask anything about the app and get a grounded answer (Priority: P1)

As a user, from any screen I can ask the Assistant how the application works or how to accomplish a
task, and I get an accurate answer that reflects this specific application — grounded in the in-app
User Guide — including a pointer to the relevant section/screen.

**Why this priority**: Trustworthy answers are the foundation of an AI-first console and the lowest-risk
half of the Assistant; they deliver value even before agentic actions.

**Independent Test**: From several screens, ask questions drawn from different User Guide topics and
confirm each answer is correct, consistent with the guide, and offers to take the user to the relevant
place.

**Acceptance Scenarios**:

1. **Given** I ask "what is X" or "how do I Y" about the app, **When** the Assistant answers, **Then**
   the answer is grounded in the User Guide and accurate for this application (not generic).
2. **Given** an answer references a place in the app, **When** it is shown, **Then** the Assistant can
   take me to the relevant section/screen.
3. **Given** a question whose answer is not in the guide or app, **When** I ask, **Then** the Assistant
   says it doesn't know rather than fabricating an answer.
4. **Given** the User Guide is updated, **When** I next ask a related question, **Then** the answer
   reflects the updated content (single knowledge source, no divergence).

---

### User Story 2 — Have the Assistant do anything I could do (Priority: P1)

As a user, I can ask the Assistant to perform a task and it carries it out through the same capabilities
the UI uses — configuring a connector, creating/running a workflow, registering/building/running an
app, responding to a review item, navigating — so the outcome is identical to my doing it by hand.

**Why this priority**: This is the defining AI-first capability and the user's explicit intent ("the
assistant should be able to do anything the user can do").

**Independent Test**: Ask the Assistant to perform a representative read action and a representative
write action; confirm the result matches performing the same action manually, and that the affected
screen reflects it.

**Acceptance Scenarios**:

1. **Given** a task I can perform in the UI, **When** I ask the Assistant to do it, **Then** it performs
   it through the same underlying capability and reports the outcome.
2. **Given** the Assistant completes an action, **When** it finishes, **Then** the result is identical
   to performing the same action manually, and the affected screen reflects the change.
3. **Given** a task spanning multiple steps, **When** I describe the goal, **Then** the Assistant can
   carry out the necessary steps (pausing for confirmation where required by US3) and report progress.
4. **Given** an action fails, **When** it does, **Then** the Assistant reports the failure with a
   reason and does not leave the system in a half-changed state it claims succeeded.

---

### User Story 3 — Confirmation and permission guardrails (Priority: P1)

As a user, I trust the Assistant because it asks before doing anything consequential and never does
something I'm not allowed to do.

**Why this priority**: Agentic action without guardrails is unsafe; these guardrails are a release
gate for US2, not an enhancement.

**Independent Test**: Ask the Assistant to perform a consequential action (e.g., delete, send, run,
deploy, approve) and confirm it asks first; attempt an action beyond the current user's permissions and
confirm it is declined.

**Acceptance Scenarios**:

1. **Given** a consequential action (destructive, outward-facing, or spending/committing), **When** I
   ask for it, **Then** the Assistant summarizes what it will do and proceeds only after I confirm.
2. **Given** a read-only or non-consequential action, **When** I ask for it, **Then** the Assistant may
   proceed without a confirmation prompt.
3. **Given** an action the current user is not permitted to take, **When** I ask for it, **Then** the
   Assistant declines and explains, rather than bypassing the limit.
4. **Given** the Assistant proposes an action, **When** I decline the confirmation, **Then** nothing is
   changed.

---

### User Story 4 — One Assistant, everywhere (Priority: P2)

As a user, there is a single Assistant across the whole console; the former Workflow-Builder-only
assistant is unified into it, so I don't deal with two different assistants.

**Why this priority**: Consistency and avoiding a divergent second assistant; valuable but the
console-wide assistant can ship before the old one is fully absorbed.

**Independent Test**: Confirm the Assistant offers its capabilities (including the former Builder
behaviour) from one unified surface, and that there is no separate, second assistant.

**Acceptance Scenarios**:

1. **Given** the console-wide Assistant, **When** I use it in the Workflow Builder, **Then** the former
   Builder assistant's capabilities are available through the same unified Assistant.
2. **Given** the product after this feature, **When** I look for assistants, **Then** there is exactly
   one (no separate Builder-only assistant remains).

---

### User Story 5 — The User Guide is the Assistant's single, current knowledge source (Priority: P1)

As a maintainer, I keep one User Guide; the Assistant always answers from it, so documentation and AI
knowledge never drift apart.

**Why this priority**: Grounding accuracy (US1) depends on a single, current source; without it answers
become unreliable.

**Independent Test**: Update a guide topic and confirm the Assistant's answer changes accordingly;
confirm the Assistant does not answer app-specific questions from a separate, conflicting source.

**Acceptance Scenarios**:

1. **Given** the in-app User Guide, **When** the Assistant answers an app-specific question, **Then** it
   uses the guide as its knowledge source.
2. **Given** the guide is edited, **When** the Assistant next answers a related question, **Then** the
   answer reflects the edit without a separate retraining/authoring step in a different place.
3. **Given** the guide does not cover a topic, **When** asked, **Then** the Assistant does not invent
   app-specific facts.

### Edge Cases

- **Ambiguous request**: The Assistant should ask a clarifying question rather than guess at a
  consequential action.
- **Partial failure mid-multi-step**: The Assistant must report what succeeded and what didn't, not
  claim overall success.
- **Confirmation fatigue**: Non-consequential actions must not over-prompt; only genuinely
  consequential ones require confirmation.
- **Stale guide / missing topic**: Answer "I don't know / not documented" rather than fabricate.
- **Long-running actions**: The Assistant should reflect in-progress vs complete and not report success
  before the underlying action finishes.
- **Secrets**: The Assistant must never reveal stored secret values, even when configuring connectors
  on the user's behalf.
- **Prompt-injection via content**: Data the Assistant reads (tickets, repo content, logs) must not be
  able to make it exceed the user's permissions or skip confirmation.

## Requirements *(mandatory)*

### Functional Requirements

**Answering (grounded Q&A)**

- **FR-001**: The Assistant MUST answer user questions about the application grounded in the in-app User
  Guide, accurately and specifically to this application.
- **FR-002**: The Assistant MUST be available from every console destination (using the panel delivered
  by spec 014).
- **FR-003**: When an answer refers to a place in the app, the Assistant MUST be able to navigate the
  user there.
- **FR-004**: When a question is outside the guide/app knowledge, the Assistant MUST say it does not
  know rather than fabricate an app-specific answer.

**Acting (agentic capability)**

- **FR-005**: The Assistant MUST be able to perform the same operations the current user can perform in
  the console, executed through the same underlying capabilities the UI uses, so outcomes are identical
  to manual operation.
- **FR-006**: The Assistant MUST report the outcome of each action (success or failure with reason), and
  the affected screen MUST reflect the change.
- **FR-007**: For multi-step goals, the Assistant MUST be able to carry out the constituent steps and
  report progress, honoring the confirmation gate (FR-008) at each consequential step.

**Guardrails**

- **FR-008**: Before performing a **consequential** action (destructive, outward-facing, or
  spending/committing — e.g., delete, send, run, deploy, approve), the Assistant MUST present what it
  will do and proceed only after explicit user confirmation. Non-consequential actions MAY proceed
  without confirmation.
- **FR-009**: The Assistant MUST be bounded to what the current user is permitted to do and see: it MUST
  NOT perform or expose any action or data the user could not access directly, and MUST decline (with an
  explanation) when asked to exceed that boundary.
- **FR-010**: The Assistant MUST never reveal stored secret values, including while configuring
  connectors on the user's behalf.
- **FR-011**: Content the Assistant ingests (e.g., ticket text, repo content, logs) MUST NOT be able to
  cause it to bypass confirmation (FR-008) or permission bounds (FR-009).

**Knowledge source**

- **FR-012**: The in-app User Guide MUST be the Assistant's single knowledge source for app-specific
  questions; the Assistant MUST NOT rely on a separate, conflicting app-knowledge source.
- **FR-013**: When the User Guide is updated, the Assistant's answers MUST reflect the update without a
  separate authoring step in a different location.

**Unification**

- **FR-014**: The existing Workflow Builder assistant MUST be unified into this single console-wide
  Assistant; no separate, divergent assistant may remain.

### Key Entities *(include if feature involves data)*

- **Assistant**: The console-wide AI agent. Holds a conversation, answers from the User Guide, and
  invokes Assistant Actions. Bounded by the current user's permissions; gates consequential actions
  behind confirmation.
- **Assistant Action**: A discrete operation the Assistant can perform, corresponding one-to-one with
  something the user can already do in the UI. Carries: a human-readable description (for confirmation),
  a consequential/non-consequential classification, the permission it requires, and a reported outcome.
- **Knowledge Source (User Guide grounding)**: The in-app User Guide content (authored in spec 014)
  treated as the Assistant's single, current source of app-specific knowledge.
- **Conversation**: The transient exchange between user and Assistant within a session (not persisted as
  a new server data model unless planning determines otherwise).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across a representative set of app-knowledge questions drawn from the User Guide, the
  Assistant answers correctly and consistently with the guide in at least 95% of cases, with no answer
  that contradicts the guide.
- **SC-002**: For a representative set of user tasks, each sampled action the Assistant performs (once
  confirmed) produces a result identical to performing it manually, with zero cases of an action the
  user could do that the Assistant cannot.
- **SC-003**: 100% of consequential actions trigger an explicit confirmation before execution.
- **SC-004**: 100% of attempts to exceed the current user's permissions are declined rather than
  performed, and no stored secret value is ever revealed.
- **SC-005**: When a User Guide topic is changed, the Assistant's answer to a related question reflects
  the change with no separate retraining/authoring step.
- **SC-006**: There is exactly one assistant in the product; the former Builder-only assistant no longer
  exists as a separate surface.
- **SC-007**: For questions outside the guide/app, the Assistant declines (says it doesn't know) rather
  than fabricating, in 100% of sampled out-of-scope questions.

## Assumptions

- **015 depends on 014.** The Assistant panel chrome and the human-readable User Guide are delivered by
  the 014 redesign; this feature makes the panel intelligent and grounds it on that guide.
- **The Assistant uses the project's governing AI framework** (Semantic Kernel) for tool/function
  calling and structured output, per the constitution's framework-first gate — concrete design is for
  `/speckit-plan`.
- **"Anything the user can do" is bounded by what the product already supports.** The Assistant exposes
  existing capabilities as callable actions; it does not gain powers the product lacks, and its breadth
  grows automatically as the product adds capabilities.
- **Permissions reflect whatever the app already enforces.** This feature does not introduce a new
  authentication/authorization system; "bounded to the current user" means it respects the access the
  app already grants (today effectively the single operator), and is structured so that if/when real
  multi-user permissions exist, the Assistant honors them.
- **The visitor's own LLM key powers the Assistant**, consistent with the rest of the app (the demo
  deployment never ships an LLM key; each user supplies their own).
- **Grounding mechanism is a planning decision** (e.g., retrieval over the guide vs. guide-in-context);
  the requirement is single-source grounding and freshness, not a specific technique.

## Out of Scope

- The console shell, sidebar/IA, theming, and the human-readable User Guide *authoring* — delivered by
  `specs/014-admin-console-ui-redesign/`.
- A new authentication/authorization system or multi-user identity model.
- New back-office product features the Assistant would call (it only calls capabilities that already
  exist; new capabilities are their own features).
- Voice or other non-text modalities.

## Dependencies

- **`specs/014-admin-console-ui-redesign/`** — provides the Assistant panel chrome and the in-app User
  Guide content this feature makes intelligent and grounds on. 015 cannot ship before 014's panel and
  guide exist.
- The project's governing AI framework (Semantic Kernel) and the existing per-run LLM key resolution.
- The existing console capabilities the Assistant exposes as actions (connectors, workflow build/run,
  apps, review queue, navigation) and the existing Workflow Builder assistant it absorbs.
- The project's testing harness for asserting answer grounding, action parity, and guardrail behaviour.
