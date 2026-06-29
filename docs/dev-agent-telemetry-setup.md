# Developer-agent telemetry setup (spec-017 US4 — org rollout)

Development AI spend is only captured if each developer's coding agent **emits usage** and **binds it
to a ticket**. The app provides the ingest contract, ledger, and rollup; the *emit + bind* lives in
each engineer's tooling — this is an organizational rollout, not something the app can enforce.

## The contract
Post each session's usage to the secret-gated endpoint:

```
POST {app}/api/telemetry/dev-usage
Header: X-Telemetry-Secret: <WebhookSecrets:Telemetry>
Body:
{
  "binding_key": "BIND-XXXXXXXX",   // the ticket's Cost Binding Key (required)
  "model": "claude-opus-4-8",
  "input_tokens": 12000,
  "output_tokens": 3400,
  "cache_read_tokens": 8000,
  "session_id": "<agent session id>",
  "occurred_at": "2026-06-29T16:00:00Z"
}
```
Response `202 { attributed: true|false }`. An unknown `binding_key` is recorded **unattributed** (still 202).

## Where the binding key comes from
The pipeline mints the **Cost Binding Key** when the ticket enters the pipeline and writes it to the
work item's `Custom.CostBindingKey` field. A developer picking up an assigned ticket reads it from
there. The session then carries it for the whole session (one binding per session — switch ticket =
new session).

## Wiring Claude Code (example)
- Configure Claude Code to export OpenTelemetry usage (token metrics) to an OTLP collector, **or** add
  a session-end hook that POSTs the usage to `/api/telemetry/dev-usage`.
- Resolve `binding_key` at session start — read it from the assigned ticket (e.g. a `/bind AB#1234`
  step that looks up the work item's `Custom.CostBindingKey`), or from a branch-name convention if your
  branches encode it.

> Cost is re-priced server-side from token counts via the app's pricing table when `cost_usd` is
> omitted, so you only need to send accurate token counts + the binding key.
