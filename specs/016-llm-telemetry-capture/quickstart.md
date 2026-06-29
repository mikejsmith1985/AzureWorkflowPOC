# Quickstart: Verify AI Usage Telemetry Capture

Proves capture works end-to-end. Implementation details live in `tasks.md`; this is a run/verify guide.

## Prerequisites

- LLM connector configured with a valid Anthropic key (Connectors → LLM), or the vaulted
  `smithbros-claude-api-key` injected.
- ADO connector configured; the telemetry preflight has run (manifest present) so target fields exist.
- A fresh `pipeline.db` (new columns are provisioned by `EnsureCreated` — move an old dev DB aside).

## Unit-test gate (fast, no I/O)

```
$DOTNET_ROOT/dotnet.exe test tests/DBAIAzure.Tests/DBAIAzure.Tests.csproj --filter "FullyQualifiedName~AdoTelemetry|FullyQualifiedName~Anthropic"
```

Expected: usage-parse, aggregate (cache sums + error count + hit-rate), pricing (cache-aware), and
write-back tests all green.

## Scenario A — Runner path captures usage

1. Run any AI-backed workflow (an `AgenticReason` node) from the Builder.
2. Open **Run History → the run**. Expected: the LLM step shows a non-null **model** and
   **input/output tokens** (previously empty). If the prompt reused cache, cache-read is non-zero.

## Scenario B — Phase-handler path captures usage

1. Trigger a Spec Kit phase (e.g. Specify) so validation makes an AI call and a work item is created.
2. Query `WorkflowExecutionEvents` for that run id. Expected: at least one `LlmCallCompleted` event
   tagged with the **phase run id** (not `"unknown"`), carrying tokens + model.

## Scenario C — Telemetry lands on the work item

1. After Scenario B completes the approved work-item write, open the ADO work item.
2. Expected fields populated: AI Session ID, AI Model Used, AI Input/Output Tokens, AI Cache Tokens
   (if cache used), AI Cache Hit Rate %, AI Estimated Cost USD, AI Tool Calls, AI Session Duration.
   **Not** populated: AI Tool Accept Rate (out of scope).

## Scenario D — Error counting

1. Temporarily configure an invalid LLM key and trigger a phase run.
2. Expected: an `LlmCallCompleted` event with `Outcome = "error"`; the run still completes the pipeline
   without crashing; AI API Errors ≥ 1 on the work item (when a work item is produced).

## Negative / non-blocking checks

- A run with no AI calls → usage fields omitted on the work item (no zeros).
- Telemetry reporter failure → run and work-item write still succeed.
