# Contract: Durable Checkpoint Store + Paused-Run Migration

**Owner**: `DBAIAzure.Storage`. **Basis**: research.md D3/D4.

## Store

- Implement **`ICheckpointStore<JsonElement>`** (namespace `Microsoft.Agents.AI.Workflows.Checkpointing`)
  over the existing EF Core store (SQLite dev / SQL Server), keyed by `runId`.
- Build the manager: `CheckpointManager.CreateJson(store, jsonOptions)`.
- Executors persist state via `OnCheckpointingAsync` (`QueueStateUpdateAsync`) /
  `OnCheckpointRestoredAsync` (`ReadStateAsync`).
- Resume across restart: `InProcessExecution.ResumeStreamingAsync(workflow, checkpoint, manager)`; the
  startup rehydration service loads paused runs' checkpoints (replaces SK-state rehydration).
- Pattern the implementation after the shipped `FileSystemJsonCheckpointStore`.

## One-time paused-run migration (cutover release, FR-006a)

- Read runs persisted in a **paused** state under SK; write an equivalent MAF **checkpoint** into the
  store with the outstanding `RequestPort` request reconstructed, so MAF resumes them **in place**.
- Idempotent; run once at deploy; logs a summary of converted/failed records.
- **Verify against representative real paused-run records** (SC-009) — 100% resume with zero lost approvals.

## Acceptance
- A run paused pre-restart resumes in place after restart (FR-006).
- 100% of pre-cutover paused runs auto-migrate and resume from their pause point (SC-009).
- No use of prerelease `Microsoft.Agents.AI.DurableTask` (FR-003) — GA checkpointing only.
