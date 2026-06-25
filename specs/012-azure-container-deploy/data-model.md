# Phase 1 Data Model: One-URL Azure Container Demo Deployment

**No persisted schema changes.** This feature changes *how the app is packaged, deployed, and
seeded*, not its data model. The existing `ConnectorConfigs` table and all entities are reused
unchanged. The "entities" below are mostly deployment/runtime concepts plus the one reused table the
boot seeder writes.

---

## Reused persisted entity (unchanged)

### ConnectorConfig (`ConnectorConfigs` table — existing)
One row per connector type. Non-secret JSON in `ConfigJson`; secrets encrypted in
`EncryptedSecretsJson` via the existing `ISecretProtector` (ASP.NET Data Protection). Written today
only by the `/settings/connectors` UI; **now also written at boot by `DemoConnectorSeeder`** for the
demo's back-office connectors.
- **Connector types seeded:** `ServiceNow` (ticketing), `AzureDevOps` (work-items), `Messaging`.
- **Connector type NOT seeded:** `LLM` (visitor-supplied only — FR-004/SC-006).
- **Validation rules:** secrets are always stored encrypted; non-secret fields (URLs, project, tool
  names) are plain; `IsConfigured` set true only when the required fields for that connector are
  present; a connector with missing env input is left unconfigured (not a partial/broken row).
- **Lifecycle:** created/overwritten on each cold start by the seeder (the DB is fresh each boot);
  a visitor may repoint any row at runtime (FR-007), last-writer-wins (FR-018); the repointed value
  lives only until the next cold start (FR-016).

---

## Deployment / runtime entities (conceptual — not persisted rows)

### Shared Deployment
The single publicly reachable ACA app behind one HTTPS FQDN.
- **Attributes:** ingress = external, `minReplicas = 0`, `maxReplicas = 1`, cpu/memory (~0.25/0.5Gi),
  target port 8080, no auth.
- **State machine:** `idle (0 replicas)` → `waking (cold start, ~10–30 s)` → `running (1 replica)` →
  `idle` after the platform default inactivity window. Wake is triggered by any HTTP request to the
  FQDN (ACA HTTP scaler).

### Visitor Session
One browser's Blazor Server circuit (+ its `WorkflowRunHub` subscriptions).
- **Attributes:** observes `run:{runId}` groups for live progress; shares the single `"demo"`
  workspace and the process-wide run state with all other sessions (no isolation, no login).
- **Lifecycle:** lives for the circuit; the visitor-entered LLM key lives in the shared connector
  store until the next cold start, then is gone (FR-016).

### Seeded Secret Set
The bundle of back-office credentials drawn from the Forge Vault at deploy time.
- **Attributes:** delivered as ACA secrets + env-var secretrefs; consumed by `DemoConnectorSeeder` at
  boot; never written to source/logs/conversation; never echoed back through the UI (masked).
- **Members (names only; values from vault):** ServiceNow base URL + credential, Azure DevOps org/
  project + PAT, Messaging endpoint/target + token. **Excludes** any LLM key.

### Connector Repointing (runtime override)
A visitor's in-app change to a connector's target/credentials.
- **Rules:** takes effect without redeploy (existing hot-reload of connector config); only the changed
  connector is affected, others keep seeded defaults (FR-008); reverts to the seeded default on the
  next cold start (FR-016).

---

## State transitions summary

### Cold start (every wake from idle)
```
ACA wakes 1 replica
  → app boots; EnsureCreatedAsync builds an empty SQLite schema (no prior state)
  → DemoConnectorSeeder reads vault-injected env
        → seeds ServiceNow / AzureDevOps / Messaging connector rows (encrypted secrets)
        → SKIPS the LLM connector
  → app serves the public URL; back-office connectors show "configured"; LLM shows "needs your key"
  → first visitor enters their LLM key (in-app) → runs work end-to-end
```

### Idle (no traffic for the platform default window)
```
ACA scales to 0 replicas → no running compute (FR-012)
  → all in-memory run state and the SQLite file are discarded (ephemeral)
  → next request repeats "Cold start" above — a fresh demo (FR-016)
```
