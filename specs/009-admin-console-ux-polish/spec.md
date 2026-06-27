# Feature Specification: Admin Console UX Polish — Configuration & Visual Parity

**Feature Branch**: `feature/admin-console-ux-polish`

**Created**: 2026-06-24

**Status**: Draft — ready for `/speckit-plan`

---

## Clarifications

### Session 2026-06-24

- Q: Which connectors count as "required" for the onboarding banner to disappear? → A: LLM only. Not every workflow uses ServiceNow or Azure DevOps — those are connector-level concerns that depend on what a given workflow needs. The LLM is the only universal dependency; without it nothing in the application functions. All four connectors remain visible on the Settings page, but only LLM health gates the onboarding banner.
- Q: Who is authorized to access the Settings page — admin-only or any authenticated user? → A: Any authenticated user. No role-based access restriction applies to the Settings page; all logged-in users may view and modify connector settings.
- Q: If two sessions save the same connector simultaneously, what is the expected conflict behavior? → A: Last-write-wins. No conflict detection or optimistic locking is required; the final save silently overwrites any concurrent change.
- Q: Should the Test Connection button have a post-result cooldown to prevent rapid retesting? → A: No cooldown. The button is non-interactive only while the test is in flight (per FR-019 spinner state); it becomes immediately clickable again once a result arrives.
- Q: How should ConnectorConfigModal.razor be retired — hard-delete or soft-deprecate? → A: Hard-delete. Remove the file and all references (including the gear-icon trigger on the Threads page) in the same PR that ships the Settings page; no deprecation period.
- Q: If the LLM health check call itself fails (exception or timeout), should the onboarding banner show or hide? → A: Show banner. A failed or timed-out health check is treated as not-yet-healthy; the banner remains visible to prompt the user to investigate.

---

## Overview

The admin console works correctly, but it looks and feels rough compared to the LangGraph reference application. The most painful point is connector configuration: when a user clicks "Edit" on a connector, they are confronted with a raw JSON textarea and asked to type something like `{"instanceUrl":"https://...","username":"jsmith"}`. This is a UX failure — a normal person cannot configure the product. The rest of the application also lacks the visual polish the reference app demonstrates: no contextual help tooltips, no color-coded connection-test feedback, no friendly "key already stored" indicators, and no smooth transitions.

This feature brings the admin console to the same level of polish as the reference application. The tech stack (Blazor Server + Tailwind CSS) is fully capable of everything described here — the gap is implementation choices, not platform limitations.

**Scope boundary**: This feature covers the Configuration section (connector settings), the Settings page, global visual-polish primitives (tooltips, validation, animation utilities), and the first-time onboarding flow. The workflow builder canvas is out of scope.

---

## User Scenarios & Testing

### User Story 1 — A non-technical administrator configures a connector without seeing JSON (Priority: P0)

Sarah is an IT administrator. She has never edited JSON in her life. Her manager has asked her to connect the application to the company's ServiceNow and Azure DevOps instances. She opens the Settings page, selects the ServiceNow connector, and sees clearly labelled fields: "ServiceNow URL", "Username", and "Password". She fills them in, clicks "Test Connection", sees a green "Connected" confirmation, and clicks "Save". She has never seen a JSON file.

**Why P0**: The existing JSON textarea experience makes the product self-service-impossible for non-developers. This is the single highest-friction point in the application.

**Acceptance Scenarios**:

1. **Given** the Settings page is open, **When** Sarah expands the ServiceNow connector, **Then** she sees distinct labelled form fields (URL, Username, Password) — no raw JSON textarea is ever shown.
2. **Given** she fills in valid values and clicks "Test Connection", **When** the test completes successfully, **Then** the button area shows a green indicator with the message "Connected".
3. **Given** she fills in an incorrect URL or wrong credentials, **When** the test completes with a failure, **Then** a red indicator appears with a human-readable explanation (e.g., "Could not reach the server — check the URL").
4. **Given** a password or API token is already saved, **When** the connector edit form opens, **Then** a "Key saved" badge is shown next to the field and the input is blank — Sarah can leave it blank to keep the existing secret or type a new value to replace it.
5. **Given** Sarah leaves the password field blank on re-save, **When** she saves, **Then** the existing secret is preserved unchanged.

---

### User Story 2 — Each connector field provides contextual guidance (Priority: P1)

James is setting up the Azure DevOps connector for the first time. He is not sure what "Organisation URL" means — is it the full URL or just the organisation name? He hovers over the info icon next to the field and a tooltip appears: "Your Azure DevOps organisation URL — for example, https://dev.azure.com/my-org". He now knows exactly what to type.

**Why P1**: Without contextual guidance, users make systematic errors (wrong format, wrong value) that produce confusing failure messages. Tooltips eliminate a class of support tickets.

**Acceptance Scenarios**:

1. **Given** any connector configuration field, **When** the user hovers over the info icon, **Then** a tooltip appears with a plain-English description and a concrete example value.
2. **Given** a tooltip is open, **When** the user moves the mouse away, **Then** the tooltip disappears within 200ms.
3. **Given** the viewport is small and the tooltip would overflow, **When** it is positioned, **Then** it flips to the opposite side automatically.

---

### User Story 3 — A first-time user is guided through setup rather than confronted with an empty screen (Priority: P1)

Alex opens the application for the first time. The LLM connector is not configured. Instead of a blank page, a friendly setup banner appears with the heading "Let's get connected" and a primary step: "Step 1: Connect your LLM — the AI pipeline needs this to run." Below the required step, the banner also lists the other connectors (ServiceNow, Azure DevOps, Teams) as optional steps labelled "configure when your workflows need it." The primary step links directly to the LLM connector panel. Once the LLM connector is configured and healthy, the banner disappears permanently.

**Why P1**: First-time setup is currently entirely undiscoverable. Users have to find the gear icon, then figure out what each JSON blob means. A guided path reduces time-to-first-successful-run from hours to minutes.

**Acceptance Scenarios**:

1. **Given** the LLM connector is not configured, **When** the application home page loads, **Then** an onboarding banner is shown with the LLM connector as the required primary step and the remaining connectors shown as optional secondary steps.
2. **Given** the banner is shown, **When** the user clicks a step, **Then** they are taken directly to the corresponding connector config panel, pre-expanded and focused.
3. **Given** the LLM connector reports a healthy status, **When** the page refreshes, **Then** the onboarding banner is no longer shown (regardless of whether other connectors are configured).
4. **Given** the user has dismissed the banner manually, **When** they return later, **Then** the banner remains dismissed (preference persisted).

---

### User Story 4 — Visual feedback and animation make the UI feel responsive (Priority: P2)

Daniel opens the Settings page, makes a change, and saves. The save button shows a spinner while the request is in flight, then briefly turns green with a checkmark. When he navigates to a new section, the content fades in smoothly rather than appearing instantly. These details signal that the application is modern and trustworthy.

**Why P2**: Micro-interactions and animation are the difference between software that feels production-grade and software that feels like a prototype. They do not change functionality but materially affect user confidence.

**Acceptance Scenarios**:

1. **Given** a save operation is in flight, **When** the user observes the Save button, **Then** it shows a loading indicator and is non-interactive.
2. **Given** a save completes successfully, **When** the button returns to its normal state, **Then** a brief (1.5s) green success state is shown before reverting.
3. **Given** a page section is navigated to, **When** it renders, **Then** the content fades in over 150ms.
4. **Given** an error occurs on any operation, **When** the response arrives, **Then** a dismissible red error banner appears with a plain-English message — no stack traces, error codes, or JSON visible to the user.

---

### User Story 5 — The configuration page layout is clean and consolidated (Priority: P1)

Maria has been using the app for two weeks. She wants to update the Teams webhook URL. She navigates to Settings and finds a single, clearly organised page with one section per connector — no modal overlaid on another page, no gear icon hunting. The URL field is right there, clearly labelled, with an example value in placeholder text.

**Why P1**: Configuration is currently split between a modal (accessible via a gear icon on the Threads page) and a dedicated Settings page. This duplication creates confusion. A single authoritative configuration home removes that ambiguity.

**Acceptance Scenarios**:

1. **Given** the application is fully set up, **When** Maria navigates to Settings, **Then** all four connectors (LLM, ServiceNow, Azure DevOps, Teams) are visible on one page — no separate modal required.
2. **Given** each connector is displayed, **When** it is not yet configured, **Then** a clear "Not yet configured" state is shown with a "Configure" call-to-action.
3. **Given** a connector is configured and healthy, **When** the section header is visible, **Then** a green "Healthy" badge is shown inline — no further action required.
4. **Given** a connector is configured but its last health check failed, **When** the section header is visible, **Then** an amber "Check required" badge is shown with a "Re-test" shortcut.

---

## Functional Requirements

### Configuration — Field-Level

**FR-001**: Every connector configuration field that holds a URL value MUST render as a `<input type="url">` (or equivalent typed input) with placeholder text showing the expected format (e.g., `https://dev.azure.com/my-org`).

**FR-002**: Every connector configuration field that holds a username or display-name value MUST render as a `<input type="text">` with a descriptive label and placeholder.

**FR-003**: Every connector configuration field that holds a password, API key, API token, or other secret value MUST render as a password input with a reveal/hide toggle button. The toggle must use accessible icon labels (aria-label).

**FR-004**: When a secret field's underlying value is already persisted, the server MUST return a sentinel value (e.g., `__KEY_STORED__`) rather than the plaintext secret. The UI MUST detect the sentinel and display a "Key saved" badge next to the field, leaving the input blank. A user can type a new value to replace the stored secret or leave the field blank to preserve it.

**FR-005**: Every configuration field MUST display an info icon that, on hover, shows a tooltip with a plain-English description of the field and at least one concrete example value.

**FR-006**: No raw JSON textarea for connector configuration is permitted anywhere in the admin console visible to a standard user. Internal persistence may remain JSON; the editing surface must be field-per-property.

### Configuration — Connection Testing

**FR-007**: Every connector section MUST include a "Test Connection" button that invokes the existing health-check infrastructure and returns a result within 10 seconds.

**FR-008**: A successful test result MUST be indicated by a green border and "Connected" label on the test button or an adjacent status indicator.

**FR-009**: A failed test result MUST be indicated by a red border and a human-readable failure reason (e.g., "Authentication failed — check your credentials"). Technical exception messages must not be shown directly; they must be translated to plain English.

**FR-010**: A partial success (connected but misconfigured, e.g., wrong model name) MUST be indicated by an amber border and a specific explanation (e.g., "Reached the server but the model 'xyz' was not found").

### Configuration — Secrets

**FR-011**: The application must never echo a stored secret back to the browser after initial entry. Once saved, the field must be blank with a "Key saved" badge.

**FR-012**: When a user leaves a secret field blank on re-save, the existing stored secret MUST be preserved without modification.

**FR-013**: A "Remove stored key" action must be available next to the "Key saved" badge to explicitly clear a stored secret.

### Onboarding

**FR-014**: When the application detects that the LLM connector is not configured or unhealthy, it MUST display a first-time-setup banner on the home page. The banner presents the LLM as a required primary step and the remaining three connectors (ServiceNow, Azure DevOps, Teams) as optional secondary steps labelled "configure when your workflows need it."

**FR-015**: Each step in the onboarding banner MUST link directly to the corresponding connector section on the Settings page, pre-scrolled and pre-expanded.

**FR-016**: When the LLM connector reports a healthy status, the onboarding banner MUST be automatically hidden on the next page load. The healthy/unhealthy state of the other three connectors does not affect banner visibility. If the LLM health check call itself fails (exception or timeout), the result is treated as not-yet-healthy and the banner remains visible.

**FR-017**: The user MUST be able to dismiss the onboarding banner manually; the dismissed state must be persisted (browser storage acceptable for this release).

### Visual Polish

**FR-018**: All page sections and major content areas MUST use a fade-in entrance animation of 150ms duration when first rendered.

**FR-019**: Save, delete, and test buttons MUST show a loading/spinner state while their operation is in flight and must be non-interactive during that time.

**FR-020**: A successful save operation MUST display a brief (1.5 second) green success indicator on or adjacent to the Save button before reverting to normal state.

**FR-021**: Error messages shown to users MUST be plain English and actionable. Technical details (stack traces, HTTP status codes, serialization errors) must not be surfaced in the main UI. Debug details may be logged server-side.

**FR-022**: All input fields MUST show inline validation errors (red border + error text below the field) immediately on blur, with no page refresh required.

**FR-023**: The Settings page layout MUST use a consistent visual hierarchy: section header with connector icon + name + health badge; expandable body with the form fields; action row (Test + Save) at the bottom of each section.

### Tooltip System

**FR-024**: An `InfoTip` component MUST be created as a reusable Blazor component that accepts a `Content` parameter (string) and optionally an `Example` parameter (string). It renders an info icon; on hover it shows a tooltip panel.

**FR-025**: Tooltips MUST be rendered at the document root (portal pattern in Blazor) to avoid clipping by `overflow: hidden` parent containers.

**FR-026**: Tooltips MUST auto-flip from top to bottom (or vice versa) when viewport space is insufficient.

---

## Success Criteria

- **SC-01**: A non-technical user with no JSON knowledge can configure all four connectors (LLM, ServiceNow, Azure DevOps, Teams) in under 10 minutes on their first attempt.
- **SC-02**: Zero raw JSON fields are visible to a standard user anywhere on the Settings or configuration pages after this feature ships.
- **SC-03**: Every connector field in the configuration form has a tooltip with a concrete example.
- **SC-04**: Test Connection results are visually distinct for three states (success/failure/partial) — a user can identify the state without reading any text.
- **SC-05**: Stored secrets are never echoed back to the browser; the "Key saved" badge is the only indication that a secret exists.
- **SC-06**: A user completing setup for the first time reaches their first healthy connector within one guided session — no documentation required.
- **SC-07**: All form actions (save, test, delete) show loading states; no button appears frozen during network operations.
- **SC-08**: All transition animations complete within 200ms and do not cause layout shift.

---

## Key Entities & Data

**ConnectorFieldDescriptor** — metadata for each field on a given connector: field key, display label, field type (Url / Text / Password / Secret / Number / Select), placeholder text, tooltip description, tooltip example, whether the field is required, and the allowed options for Select types.

**ConnectorFormState** — transient UI state per connector: current draft field values (excluding secrets not yet modified), test result (Untested / Success / Partial / Failed), test message, save status (Idle / Saving / SavedOk / SavedError), and whether the section is expanded.

**SecretSentinel** — the string constant returned by the server when a secret value is already stored (`__KEY_STORED__`). The UI detects this and renders the "Key saved" badge instead of a field value.

**OnboardingState** — tracks which required connectors are configured and healthy, and whether the user has manually dismissed the banner.

---

## Assumptions

- The existing `IConnectorHealthChecker` and `IConnectorConfigRepository` interfaces (from spec 002) remain unchanged; this feature consumes them, it does not replace them.
- The existing `ISecretProtector` used by `ConnectorSettings.razor` already encrypts secrets at rest; this feature does not change secret storage, only the editing surface.
- The four connectors (LLM, ServiceNow, Azure DevOps, Teams) each have a fixed, known field schema. A `ConnectorFieldDescriptor` table for each can be hardcoded in this release; a schema-driven approach is a follow-on.
- "Normal user" in this spec means a person comfortable using a web form but not comfortable editing JSON.
- The existing gear-icon modal (`ConnectorConfigModal.razor`) will be hard-deleted — the file and all references (including the gear-icon trigger on the Threads page) are removed in the same PR that ships the Settings page. No deprecation period or compatibility shim is provided.
- The Settings page is accessible to any authenticated user; no role-based authorization gate is needed for this feature. Acceptance tests must not assume an admin-only route guard.
- Concurrent saves to the same connector follow last-write-wins semantics; no optimistic locking, ETags, or conflict detection is in scope for this feature.

---

## Out of Scope

- Workflow builder canvas UX changes
- Adding new connector types
- Dynamic/schema-driven field discovery from the server (hardcoded field descriptors are acceptable for this release)
- Light mode (the application is dark-only; dark-mode parity with the reference app is sufficient)
- Conversational configuration (chat-driven setup as seen in the reference app)
- Font size controls
- Mobile / responsive layout beyond what Tailwind provides by default
