# Phase 0 Research: One-URL Azure Container Demo Deployment

The behavioral target is the reference LangGraph app at `C:\ProjectsWin\DBAI`. Research = studying
the reference's actual deployment and reconciling it with the current .NET app's posture. The spec's
three clarifications (ephemeral reset, fully public, shared workspace) plus this study resolve all
open decisions; no `NEEDS CLARIFICATION` markers remain.

---

## Decision 1 — Hosting: Azure Container Apps, single container, local `az` CLI deploy

**Finding (reference):** `C:\ProjectsWin\DBAI\deploy\aca\` deploys a single combined container to
**Azure Container Apps** via a local bash script (`deploy.sh`) using the `az` CLI; images live in
**Azure Container Registry** (built/pushed locally, no Docker Hub). The ACA create call is
`--target-port 3000 --ingress external --cpu 0.25 --memory 0.5Gi --min-replicas 0 --max-replicas 1`,
with a post-step that re-asserts `--min-replicas 0 --max-replicas 1`. There is **no** Bicep/ARM/
Terraform/`azure.yaml` and **no** GitHub Actions deploy (an Actions workflow exists but only pushes
an unrelated image to GHCR).

**Decision:** Mirror exactly — ACA + ACR, deployed by a **local script**. Because this environment is
Windows/pwsh, author `deploy/aca/deploy.ps1` (same model and `az` commands as the reference's bash
script). Use `--ingress external --min-replicas 0 --max-replicas 1` with comparable CPU/memory.

**Rationale:** Exact reference parity (the spec's core ask); satisfies Constitution Article VIII
(local pipeline, no Actions) and Article I (proven topology). Single container keeps the demo simple
and cheap.

**Alternatives considered:** App Service (no native scale-to-zero), Container Instances (no built-in
wake-on-request HTTP scaler), Bicep/azd (the reference deliberately uses a plain `az` script — adding
IaC would diverge and add ceremony for a demo). All rejected for parity + simplicity.

---

## Decision 2 — Scale-to-zero & idle window: ACA platform default (reference parity)

**Finding (reference):** `--min-replicas 0` plus ACA's built-in HTTP ingress scaler provides
wake-on-request. The reference sets **no explicit idle window** (no KEDA `cooldownPeriod`, no custom
scale rule); idle scale-in relies on the **ACA/KEDA platform default (~300 s / 5 min after the last
request)**. The script's own note: "First hit after idle cold-starts in ~10-30s; the app scales back
to zero when unused."

**Decision:** Mirror the reference — rely on the **platform default** scale-to-zero; do not invent a
bespoke timer. The spec's "10–15 min" is the user's recollection; "exactly the same as the reference"
takes precedence, and the reference uses the default. If a specific window is later desired, it is a
one-line KEDA cooldown addition — noted, not built now.

**Rationale:** Framework-first (Article VII — use the platform scaler), exact parity, lowest
complexity. FR-012/FR-013/SC-004 are satisfied by the platform behavior.

**Alternatives considered:** Custom KEDA cron/cooldown rule to force 10–15 min — rejected as a
divergence from the reference and unnecessary for a demo; revisit only if the user insists on a
specific window.

---

## Decision 3 — Ephemeral state via container-local SQLite (no volume)

**Finding (reference):** SQLite lives on the container's ephemeral filesystem with **no mounted
volume**; every cold start resets to defaults; `startup_configure.py` re-seeds infra connectors from
env on each boot, but the **LLM key is deliberately not re-seeded**. **Finding (our app):** EF Core
SQLite at `Storage:SqlitePath` (default `pipeline.db` under ContentRoot) — already container-local
and ephemeral; `EnsureCreatedAsync` builds the schema on each boot. The Data Protection key ring
defaults to a local, ephemeral path too.

**Decision:** Do **not** mount a volume for SQLite or the key ring. Embrace ephemerality: each cold
start recreates an empty DB, the boot seeder re-populates connectors from env, and the visitor
re-enters their LLM key — exactly FR-016 and the reference. Pin `SetApplicationName` on Data
Protection for in-lifetime stability, and persist the key ring to an **ephemeral, non-mounted** path
(configurable via `DataProtection:KeyRingPath`, set in the Dockerfile) so the keys reset on every cold
start along with the rest of the demo state; encrypt/decrypt only needs to work within one container
lifetime.

**Rationale:** This is the rare case where the spec explicitly wants ephemerality, so the "persist the
DB / escrow the keys" instinct is wrong here. Container-local SQLite gives reference-exact
reset-on-cold-start for free (Article VII — reuse, build nothing). FR-016 also forbids requiring a
persistent DB.

**Alternatives considered:** Azure Files volume for SQLite / Blob-backed key ring — rejected: would
*preserve* state across restarts, contradicting FR-016 and the reference. In-memory SQLite — rejected:
unnecessary; file-on-ephemeral-FS already resets and avoids provider quirks.

---

## Decision 4 — Boot-time connector seeding from env (the core code gap)

**Finding (our app):** Connectors are written to `ConnectorConfigs` **only** via the
`/settings/connectors` UI; nothing reads env/appsettings to create connector rows at boot. Config
values (Anthropic key, ADO PAT) act only as runtime *fallbacks* inside the kernel factories — they
never create a connector row. **Finding (reference):** `startup_configure.py` runs on every cold
start and PUTs env-sourced config into the connectors (Jira/ServiceNow/Discord), excluding the LLM
key.

**Decision:** Add a startup **`DemoConnectorSeeder`** (the only new runtime component). On each boot
(post-`Build()` scope in `Program.cs`, after `EnsureCreatedAsync`) it reads vault-injected env vars
and writes the **ServiceNow (ticketing)**, **Azure DevOps (work-items)**, and **Messaging** connector
rows via the existing `IConnectorConfigRepository`, encrypting secrets through the existing
`ISecretProtector`. It **explicitly excludes the LLM connector** (FR-004/SC-006). It is idempotent
(safe to re-run; on the always-fresh ephemeral DB it simply (re)creates the rows). Missing env vars
for a connector → that connector is left unconfigured (logged at info, no secret echoed) rather than
crashing the boot.

**Rationale:** Fills the one documented framework gap (Article VII); reuses the connector repository
and encryption seam (no new store, no new crypto). Mirrors the reference's `startup_configure.py`.
Keeps the LLM as the single visitor-supplied credential.

**Alternatives considered:**
- *Seed connectors by having the kernel factories read config fallbacks* — rejected: that only
  affects LLM/kernel calls, does not populate the connector rows the UI and health checks read, and
  would not make the connectors show as "configured" to the visitor.
- *Bake a pre-populated SQLite into the image* — rejected: embeds secrets in an image layer
  (violates Article IX) and breaks ephemeral re-seed semantics.
- *Seed via the UI after each cold start* — rejected: manual, defeats the "works out of the box"
  promise (FR-005).

---

## Decision 5 — Secret delivery: Forge Vault → ACA secrets/env at deploy time

**Finding (reference):** A gitignored `team.env` (`team.env.example` is the committed template) holds
`KEY=VALUE` lines; on first ACA create, `deploy.sh` converts each into an **ACA secret + env-var
secretref** (`--secrets name=value --env-vars KEY=secretref:name`). No Azure Key Vault is used.
Secrets are set only on first create; later updates only swap the image. **Finding (our app):** the
Forge Vault is the mandated zero-knowledge secret source (global constitution + Article IX); the app
already encrypts connector secrets at rest and masks them in the UI; a `KeyVault:Uri` +
`DefaultAzureCredential` overlay also exists if ever wanted.

**Decision:** Source the back-office secret values from the **Forge Vault at deploy time** (via the
vault injection flow), write them into ACA secrets + env-var secretrefs from a **gitignored
`deploy/aca/team.env`** (committed `team.env.example` lists only the *names*). The agent never handles
plaintext: `seed-secrets.ps1` injects vault values into the local shell/`team.env` and the deploy
script passes them to `az containerapp ... --secrets/--env-vars`. The running app's
`DemoConnectorSeeder` reads those env vars. The UI continues to mask stored secrets (FR-009). The LLM
key is **not** among the seeded secrets (FR-004).

**Rationale:** Exact reference parity for the env→secret mechanism, while honoring Article IX
zero-knowledge by sourcing from the vault and never committing values. Avoids the Key Vault round-trip
the reference also avoids (simpler demo), though `KeyVault:Uri` remains available as an alternative.

**Alternatives considered:**
- *Azure Key Vault + managed identity for connector secrets* — viable and slightly more "production,"
  but diverges from the reference and adds identity/RBAC setup for a throwaway demo; kept as a
  documented option, not the default.
- *Plaintext `--env-vars` (no `--secrets`)* — rejected: would expose values in the ACA env listing;
  ACA secrets keep them masked.

---

## Decision 6 — Concurrency & real-time: existing Blazor Server + SignalR on a single replica

**Finding (our app):** Blazor Server (circuit hub) plus an explicit `WorkflowRunHub` at
`/hubs/workflow-run` (clients join `run:{runId}` groups) delivers live progress; run state lives in
**singleton** `WorkflowExecutionOrchestrator` (in-memory `ConcurrentDictionary`); there is **no auth**
and a single hardcoded `"demo"` owner, so all visitors already share one workspace and one process-
wide run state. **Finding (reference):** single instance (`--max-replicas 1`), shared global config,
real-time via 3 s polling + SSE; sessions share state.

**Decision:** Keep the existing SignalR-based real-time path unchanged and pin **`--max-replicas 1`**.
A single replica is *required* for correctness (in-memory singletons + SignalR with no backplane) and
simultaneously delivers the shared-workspace model (FR-018) and concurrent live updates (FR-010/
FR-011). No backplane (Redis), no per-user isolation, no auth — matching the reference's demo posture
(FR-017).

**Rationale:** Framework-first (reuse the app's real-time stack); the single-replica constraint is
both a technical necessity and an exact behavioral match to the reference. Implementation transport
(SignalR vs the reference's polling/SSE) need not match — only observable behavior (the spec says so).

**Alternatives considered:** Multi-replica + Redis SignalR backplane + external run-state store —
rejected: large rework, contradicts the single-shared-instance reference model, unnecessary for a
two-viewer demo. ARR/session affinity — moot at one replica.

---

## Decision 7 — The visitor-supplied LLM key must reach EVERY LLM consumer (not just execution paths)

**Finding (our app):** The execution kernel factories re-read the LLM key + model from the
`ConnectorType.LLM` row in the DB on every run — `PipelineOrchestrator` (`Program.cs:108–148`),
`PhaseHandlerOrchestrator` (`:174–218`), and `WorkflowExecutionOrchestrator` (`:304–359`) — so running
tickets/workflows uses the visitor-entered key (hot-reload, FR-014). **But two design-time LLM
singletons are constructed once at startup from `builder.Configuration["Anthropic:ApiKey"]`:** the
Workflow Builder AI design assistant `IChatCompletionService` (`:266–267`) and the Node Realization
`IStructuredCompletionService` (`:296–297`). Because the demo deliberately does **not** seed the LLM
key (Decision 4 / FR-004), that config value is empty, so these two singletons capture an empty key and
**never** observe the key the visitor later saves in the UI (it lives in `ConnectorConfigs`, which they
don't read) — not even across a restart.

**Decision:** Route those two singletons through the **same DB-first → config-fallback resolution the
per-run factories already use** (a lazy/factory wrapper that reads the `ConnectorType.LLM` secret/model
per call via `IConnectorConfigRepository` + the existing decrypt seam). After the change, the builder's
AI assistant and node realization work the moment the visitor saves their key, with no restart.

**Rationale:** FR-003 / FR-004 / SC-006 and reference parity mean the single user-entered key powers
**every** LLM feature. Without this, a visitor who "supplies only their LLM key" finds the AI builder
assistant and node realization silently dead — a parity and UX break against the reference, where one
key drives everything. The fix reuses the per-call resolution pattern already proven three times in this
same file — no new abstraction (Article VII).

**Alternatives considered:**
- *Leave the singletons reading startup config and document the limitation* — rejected: breaks the
  spec's plain promise and the reference's single-key model.
- *Seed a default LLM key from the vault so the singletons work at startup* — rejected: violates
  FR-004 / SC-006 (the LLM key must never be pre-seeded).
- *Make the two services scoped and resolve the key in `OnInitialized`* — rejected: a larger refactor
  than threading the existing key-resolution delegate, which is the established pattern here.

---

## Cross-cutting notes

- **Port binding:** the app hardcodes no port; set `ASPNETCORE_URLS=http://+:8080` in the image and
  `--target-port 8080` on ACA (the reference uses 3000 behind nginx; we expose Kestrel directly, so
  any agreed port works — 8080 chosen).
- **No reverse proxy needed:** the reference bundles nginx+supervisor to run 3 Python processes in one
  container. Our app is a single Kestrel process, so the container runs `dotnet DBAIAzure.Web.dll`
  directly — simpler, no nginx/supervisor.
- **Reproducibility (FR-014):** everything needed to redeploy is committed (`Dockerfile`,
  `deploy/aca/*`, `team.env.example`); only the real `team.env` (values) is gitignored.
- **Testing boundary:** cloud idle/wake/FQDN are validated via `quickstart.md` on a real deployment;
  the seeder and container boot are covered by xUnit + a local Docker smoke run (see plan Article V).
