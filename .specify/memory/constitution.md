# AzureWorkflowPOC — Project Constitution

> Scaffolded for Spec-Driven Development (GitHub Spec Kit) in a .NET 8 / Semantic Kernel
> Process Framework solution. This file is the single source of truth for this project's
> binding rules. Every Spec Kit stage — `/speckit-specify` → `/speckit-plan` →
> `/speckit-tasks` → `/speckit-implement` — MUST read and obey these Articles.
> Quality Mode: **BEST** | Project Type: **dotnet**

## Article I — Prime Directive

Take the BEST route, never the fastest. Production-readiness outranks speed; a "quick but
dirty" solution is strictly forbidden. Parallelize independent work and reserve premium models
for architecture and complex tasks.

## Article II — Process Protection (HIGHEST SEVERITY)

NEVER use wildcard process kills (e.g. `Get-Process -Name "dotnet*" | Stop-Process` or
`taskkill /IM dotnet.exe`). Multiple `dotnet` processes (the build server, the running
`DBAIAzure.Runner`/`DBAIAzure.Web`, test hosts, and possibly the agent's own host) share that
image name; a wildcard kill destroys unrelated work and can terminate the agent's session.
ALWAYS target a specific PID: `Stop-Process -Id <PID>`.

## Article III — Branching (GitHub Flow)

All work happens on feature branches: `feature/*`, `fix/*`, `chore/*`, `docs/*`.
Never commit directly to `main`. Every merge to `main` requires a Pull Request.
Branch names are lowercase, hyphenated, and descriptive — scoped to one concern.

## Article IV — Code Quality

Names are self-documenting: no single-letter variables (except `i`/`j`/`k` loop iterators).
Public members are `PascalCase`, locals and parameters `camelCase`, private fields `_camelCase`,
interfaces `IPascalCase`. Booleans read as predicates (`isValid`, `hasItems`, `canRetry`).
Asynchronous methods end in `Async` and flow a `CancellationToken`. Enable and honor nullable
reference types — never suppress a null warning with `!` to silence the compiler. No magic
numbers. Methods stay focused (prefer under ~40 lines); use guard clauses over deep nesting.
Every public type/member carries an XML doc comment explaining the "why," readable by a
non-developer.

## Article V — Testing (Three-Layer Separation)

Tests use **xUnit** and run via `dotnet test`. Unit tests are 100% mocked (Moq or hand-rolled
fakes), have no I/O, and run in milliseconds. Integration tests exercise real infrastructure
(real Azure emulators/Storage, a real Semantic Kernel, a real HTTP listener) — never mocked
drivers. E2E tests use **Playwright** (Microsoft.Playwright, headless Chromium) — launched via
`scripts/run-e2e.ps1`, never by building the binary directly. The WebAppFixture starts the
real Kestrel server on port 5099 and Playwright connects via genuine HTTP/SignalR. Every
navigation tab and key interactive element MUST have a Playwright test before a feature is
considered shippable. Follow Red → Green → Refactor: the failing test is written before the
implementation.

## Article VI — Documentation Discipline

`CHANGELOG.md` is the single source of truth for what changed; update it in every PR that
changes behavior. Do not create auxiliary summary or status documents. The per-feature
`specs/<feature>/` tree produced by the Spec Kit pipeline is exempt — those are pipeline
artifacts, not ad-hoc status docs.

## Article VII — Framework-First Gate (Microsoft Agent Framework)

Before building any infrastructure, confirm the governing framework does not already provide it.
This solution is built on the **Microsoft Agent Framework (MAF)** — the generally-available successor
to the Semantic Kernel Process Framework (see spec-019). Reach for MAF's primitives before hand-rolling:
- **Orchestration / state** → MAF **Workflows** (`WorkflowBuilder`, `Executor`, typed message edges /
  `AddSwitch` routing); do not build a bespoke state machine or event bus.
- **Human-in-the-loop** → **`RequestPort`** + `RequestInfoEvent` + `SendResponseAsync` (suspend → await
  external response → resume); do not invent a custom pause/resume or polling loop.
- **Durable pause/resume** → MAF **checkpointing** (`CheckpointManager` + `ICheckpointStore`); do not
  hand-roll snapshot persistence.
- **Model access / structured output** → `Microsoft.Extensions.AI` **`IChatClient`** with
  `ChatResponseFormat.ForJsonSchema` and tool calling; do not parse free-text model output by hand, and
  do not depend on provider-specific client types in orchestration code.
- **Cross-cutting capture / DI** → `IChatClient` middleware (`DelegatingChatClient` /
  `ChatClientBuilder.Use`) and the framework's registration; do not build a parallel filter registry.

Only **GA/stable** MAF packages belong in the execution path — no experimental/pre-release packages or
opt-out pragmas (Prime Directive; spec-019 FR-003). Build custom only against a documented gap, and
record the one-line justification at the custom component. This gate MUST pass before `/speckit-plan`
finalizes a technical approach.

> Migration note: the Semantic Kernel Process Framework was the prior governing framework; spec-019
> supersedes it with MAF. Legacy specs (≤018) reference SK primitives — those remain valid history.

## Article VIII — Release Discipline

Releases are deliberate and reproducible. Tag the release, build via `dotnet publish` /
`dotnet pack` with the pinned SDK (`global.json`), and produce versioned artifacts. Never ship
from an uncommitted working tree.

## Article IX — Secrets & Configuration

Secrets are never hard-coded or committed. Read them from configuration (`IConfiguration`, user
secrets in development, Azure Key Vault / environment in deployed environments). A secret value
must never enter source, a log, or the conversation. Connection strings and API keys are
referenced by name, resolved at runtime.

## Article X — Verification & Proof

"It compiles" and "the API returned 200" are not proof. Verify behavior with evidence: a passing
test that exercises the path, observed process output, or a real round-trip. This Article maps to
the `/speckit-analyze` consistency gate.

## Article XI — Output & Dashboard Restraint

Do not narrate internal phase names to the user. Do not emit Markdown summaries unless explicitly
requested. Keep generated/scratch output out of the committed tree.
