# Research: Admin Console UX Polish — Configuration & Visual Parity

## Decision 1 — Tooltip Portal Strategy

**Decision**: Blazor-level portal via a scoped `TooltipService` rendered in `MainLayout.razor`.

**Rationale**: Blazor Server has no DOM-level `createPortal` API. The established .NET-idiomatic pattern is a scoped service that holds a `RenderFragment?` + screen coordinates and is injected into both `InfoTip.razor` (to show/hide) and `MainLayout.razor` (to render the floating panel at the layout root, above all `overflow: hidden` ancestors). Positioning uses the existing `getBoundingClientRect` JS interop function already registered in `_Host.cshtml`.

**Alternatives considered**:
- JS-only tooltip library (Tippy.js, Floating UI) — rejected: adds a CDN dependency not currently present; couples tooltip lifecycle to JS rather than Blazor state.
- CSS-only tooltip (`position: absolute` on the parent) — rejected: clipped by `overflow: hidden` containers already present in the settings sections; does not satisfy FR-025.
- Popover API (`popover` attribute) — rejected: browser support incomplete as of Blazor Server's target runtime; requires polyfill.

---

## Decision 2 — LocalStorage Access in Blazor Server

**Decision**: Add two thin JS interop functions (`window.localStorageGet`, `window.localStorageSet`) to the existing inline script block in `_Host.cshtml`; call them via `IJSRuntime` from `OnboardingStateService`.

**Rationale**: The project already uses inline `_Host.cshtml` script functions for all JS interop (no separate `.js` files in `wwwroot`); this pattern is consistent with the existing `mermaidRender`, `scrollToBottom`, and `getBoundingClientRect` functions. FR-017 explicitly permits browser storage for this release. No third-party library is needed.

**Alternatives considered**:
- Server-side `IConnectorConfigRepository` dismissed-flag column — rejected: scoped to browser instance per spec; storing per-user server-side requires auth context the spec does not mandate.
- Blazor Protected Browser Storage (`ProtectedLocalStorage`) — rejected: introduces encryption overhead and a new NuGet dependency for a non-sensitive boolean flag; plain `localStorage` is appropriate here.

---

## Decision 3 — Connector Field Schema Representation

**Decision**: Hardcoded static `IReadOnlyList<ConnectorFieldDescriptor>` tables, one per `ConnectorType`, implemented as a `ConnectorFieldSchema` static class in `DBAIAzure.Core`.

**Rationale**: The spec explicitly declares "A `ConnectorFieldDescriptor` table for each can be hardcoded in this release; a schema-driven approach is a follow-on." The four connectors have stable, known fields derived directly from the existing typed config records (`ServiceNowConnectorConfig`, `AzureDevOpsConnectorConfig`, `LlmConnectorConfig`). Static tables are testable (the table completeness can be asserted), zero-runtime-cost, and easy to extend later.

**Field mapping**:

| Connector | Key | Label | Type | Stored In |
|-----------|-----|-------|------|-----------|
| ServiceNow | InstanceUrl | ServiceNow URL | Url | NonSecretConfig |
| ServiceNow | Username | Username | Text | NonSecretConfig |
| ServiceNow | Password | Password | Secret | SecretsBlob |
| AzureDevOps | OrganizationUrl | Organisation URL | Url | NonSecretConfig |
| AzureDevOps | ProjectName | Project Name | Text | NonSecretConfig |
| AzureDevOps | PersonalAccessToken | Personal Access Token | Secret | SecretsBlob |
| LLM | ProviderEndpoint | Provider Endpoint | Url | NonSecretConfig |
| LLM | ModelName | Model Name | Text | NonSecretConfig |
| LLM | ApiKey | API Key | Secret | SecretsBlob |
| Teams | WebhookUrl | Webhook URL | Secret | SecretsBlob |

**Alternatives considered**:
- JSON-driven schema in `appsettings.json` — rejected: overkill for four fixed connectors; breaks the spec's explicit "hardcoded is acceptable" intent.
- Reflection over typed config record properties — rejected: loses tooltip metadata; brittle against property renames.

---

## Decision 4 — SecretSentinel Detection and Preservation

**Decision**: Use the `ConnectorConfig.HasSecrets` bool (already returned by `IConnectorConfigRepository.GetAsync`) to set a sentinel string `"__KEY_STORED__"` in the form state. On save, if the field value equals the sentinel, pass `null` for `plaintextSecretsJson` to `SaveAsync`, which the existing repository implementation interprets as "preserve existing secret."

**Rationale**: `IConnectorConfigRepository.SaveAsync` already accepts `null` for `plaintextSecretsJson` as a no-op for secrets (per existing implementation). The sentinel bridges the gap between "field is blank because nothing was entered" and "field is blank because a secret is already stored." No repository changes are required.

**Alternatives considered**:
- A separate `bool preserveSecret` parameter on `SaveAsync` — rejected: would require a breaking change to the existing interface, which the spec explicitly prohibits (Assumption: interfaces remain unchanged).
- Returning a masked value (e.g., `"••••••••"`) — rejected: a masked value leaks information about secret length and could be accidentally re-submitted as the new secret.

---

## Decision 5 — Animation Strategy

**Decision**: Tailwind CDN utility classes (`transition-opacity`, `animate-pulse`, custom `fade-in` keyframe) added to `workflow-canvas-animations.css`.

**Rationale**: The project already uses Tailwind CDN with no build step. Tailwind's CDN mode generates all utility classes on demand. The existing `workflow-canvas-animations.css` file is the established home for custom animation keyframes. New utilities (`@keyframes fade-in`, `@keyframes success-flash`) follow the same pattern as existing `.edge-flow-active` and `.run-btn-ready` entries.

**FR mappings**:
- FR-018 fade-in: `.section-enter` with `@keyframes fade-in` (0 → 100% opacity, 150ms)
- FR-019 spinner: Tailwind `animate-spin` on an SVG spinner element; button `disabled` attribute
- FR-020 success flash: `.btn-success-flash` with `@keyframes success-flash` (green → normal, 1.5s)
- FR-022 inline validation: Tailwind `border-red-500 text-red-400` applied on blur via Blazor event handler

**Alternatives considered**:
- CSS transition on `:enter` pseudo-class — not available in Blazor Server (no DOM mutation observer).
- Alpine.js or Framer Motion — rejected: JS animation libraries not present; Tailwind + CSS keyframes sufficient.

---

## Decision 6 — ConnectorConfigModal.razor Retirement

**Decision**: Hard-delete `ConnectorConfigModal.razor` and `ConnectorSection.razor` in this PR alongside their trigger sites.

**Rationale**: Confirmed by clarification Q4. The spec Assumption states "the Settings page becomes the single canonical configuration surface." The constitution (Article IV) prohibits backwards-compatibility shims. The modal trigger must be found and removed at the same time — the Explore agent confirmed the modal is opened via `Open()` method calls from the workflow builder and other entry points; these references must be deleted in the same atomic commit.

**Risk**: Any page currently relying on `ConnectorConfigModal.razor` will fail to compile. This forces discovery of all call sites at compile time, which is preferable to a silent runtime regression.

---

## Decision 7 — New Component Location

**Decision**: New Settings-specific Razor components live in `src/DBAIAzure.Web/Components/Settings/`.

**Rationale**: Existing components are organized by domain concern under `Components/WorkflowBuilder/`. A parallel `Components/Settings/` directory follows the established convention and keeps the `Shared/` directory for layout-level components only.

**New components**:
- `InfoTip.razor` — tooltip trigger icon
- `OnboardingBanner.razor` — first-time setup banner
- `ConnectorFieldEditor.razor` — renders a single `ConnectorFieldDescriptor` as a form field

**New services**:
- `TooltipService.cs` in `src/DBAIAzure.Web/Services/`
- `OnboardingStateService.cs` in `src/DBAIAzure.Web/Services/`

**New core model**:
- `ConnectorFieldDescriptor.cs` in `src/DBAIAzure.Core/Models/`
- `ConnectorFieldSchema.cs` in `src/DBAIAzure.Core/Models/` (static factory)
- `ConnectorFormState.cs` in `src/DBAIAzure.Core/Models/` (transient UI state record)
