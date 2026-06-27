# Contract: Assistant Panel (presentation chrome)

`AssistantPanel.razor` is a shell-level right rail. In **this** feature it is presentation only; the
intelligent/agentic behaviour is feature 015. The panel MUST be built so 015 can wire intelligence in
without restructuring the shell.

## Presentation contract

- **C-AP-1**: The panel renders a header (title, identity label, and collapse/expand/close controls),
  intro text, suggestion chips, and a message input with a send affordance (FR-013).
- **C-AP-2**: The panel is collapsible/hideable; when collapsed the content region reflows to reclaim
  the width; it can be re-opened (FR-014).
- **C-AP-3**: Open/closed state is read from `UiPreference` on load and written on change, persisting
  across navigation and reload (FR-014 + data-model).
- **C-AP-4**: In the Workflow Builder, the panel hosts the existing `WorkflowChatPanel` with its
  current parameters/callbacks, preserving today's behaviour (FR-015). The existing chat panel's
  parameter surface (`IsOpen`, `Workflow`, `CodeGenerator`, `AvailabilityMonitor`, `DesignSkillService`,
  `DiffService`, `OnCodeSaved`, `OnCloseRequested`, `OnGenerationResultReady`) is passed through
  unchanged.
- **C-AP-5**: Outside the Builder, the panel shows the chrome (intro/chips/input) without claiming
  intelligent capability; suggestion chips/input are inert or route the user to where a capability
  lives. This feature MUST NOT block 015 from making the input/chips intelligent.

## Out of scope here (→ feature 015)

Answering questions, performing actions, User-Guide grounding, confirm-gates, permission bounds.

## E2E acceptance

- On a non-Builder destination: assert panel header, intro, chips, and input are present.
- Collapse the panel; assert content reflows and the panel reopens; reload and assert the
  collapsed/open state is restored.
- On the Builder: assert the existing chat panel still functions (generate/diff/save flow unchanged).
