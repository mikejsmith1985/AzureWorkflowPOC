# Quickstart: Validate the Admin Console Redesign

A run/validation guide proving the feature end-to-end. Implementation details live in `tasks.md`;
this is how you confirm it works. Maps each step to spec Success Criteria (SC-*) and contracts.

## Prerequisites

- .NET 8 SDK (repo `global.json`).
- A browser. (No LLM key needed — the Assistant is presentation-only here; intelligence is feature 015.)

## Run the app locally

```powershell
# From repo root
./scripts/start-web.ps1          # or: dotnet run --project src/DBAIAzure.Web
# App serves on the dev URL (Playwright fixture uses http://localhost:5099)
```

## Manual validation walkthrough

1. **Shell present everywhere (SC-001, C-SHELL-1/2)** — Open the app. Confirm the left sidebar
   ("Admin Console / Control Plane", five sections + separated User Guide + version footer), the top
   bar (title, text-size control, connection indicator), and the right Assistant rail. Click through
   Monitor → Automation → Configuration → Repos & Apps; the shell stays put and the active section
   highlights, one at a time.
2. **Graph folded in (SC-002, C-NAV-3)** — Confirm there is **no** "Graph" sidebar entry. Visit
   `/graph` directly → you land in the Workflow Builder, not a 404. Open a saved workflow in the
   Builder → the graph shows *that workflow's* nodes/edges. Confirm the old fixed intake-pipeline
   diagram appears nowhere.
3. **No orphaned screens (SC-003, C-NAV-1)** — Reach every pre-redesign screen from the sidebar/sub-tabs:
   Threads, Run History, Review Queue, Workflow Builder, Workflow Gallery, Apps (+ a detail), Connector
   settings, New Ticket, User Guide. None 404s.
4. **Assistant panel (C-AP-1/2/3/4)** — On a non-Builder screen, confirm the panel header, intro,
   chips, and input render. Collapse it → content reflows; reopen it. Reload → the open/closed state is
   restored. In the Builder, confirm the existing chat panel still generates/diffs/saves.
5. **Text size + connection (SC-006, FR-018/019)** — Change text size → content rescales without
   breaking layout; reload → choice restored. Observe the connection indicator showing connected + host;
   (optional) stop the server briefly to see it flip to disconnected.
6. **Theming tokens (SC-005, C-THEME-1/2)** — Confirm a consistent dark theme across all regions. Spot
   check (or grep) that redesigned screens use semantic token classes, not raw `gray-950`/`cyan-400`.
7. **Visual family match (SC-004)** — Side-by-side with the gh #31 screenshots: sidebar + top bar +
   right rail, accent-driven active states, and card/chip language are all present.
8. **User Guide coverage (SC-009)** — Open User Guide; confirm it documents each primary section and
   key task.
9. **Accessibility (FR-024/025)** — Tab through shell controls (visible focus); enable reduced-motion
   and confirm transitions are suppressed.

## Automated validation

```powershell
./scripts/run-e2e.ps1            # Playwright E2E (WebAppFixture @ :5099)
dotnet test tests/DBAIAzure.Tests/DBAIAzure.Tests.csproj   # unit (incl. UiPreferenceService)
```

Expected: new E2E classes pass — `ShellNavigationTests`, `GraphRedirectTests`, `AssistantPanelTests`,
`TextSizeAndConnectionTests`, `UserGuideTests` — plus updated existing per-page tests, and
`UiPreferenceServiceTests` (SC-008). Article V: a Playwright test exists for each redesigned
destination and each key shell control.

## Definition of done (this feature)

- All steps 1–9 pass manually; all automated tests green.
- No standalone Graph page/diagram remains; `/graph` redirects.
- No hard-coded theme colours on redesigned screens (token-based).
- `CHANGELOG.md` updated (Article VI). The intelligent Assistant remains feature 015.
