# Contract: AI Prompt Templates & Structured Output

All three AI calls use `IStructuredCompletionService.GetStructuredAsync<T>(system, user, toolName, toolDesc,
schemaJson, ct)` (`ChatResponseFormat.ForJsonSchema`) — no free-text parsing (D8, Article VII). Templates are
config (`ai.*_prompt_template`) with `{{placeholder}}` interpolation. On a malformed/parse failure: one bounded
corrective retry, then manual exit (FR-030).

Placeholders: `{{dor_document}}`, `{{ticket_fields}}`, `{{failed_criteria}}`, `{{human_response}}`,
`{{iteration_count}}`, `{{ticket_url}}`, `{{ai_editable_fields}}`, `{{field_updates}}`.

## 1. Review — `DorReviewResult`

System prompt evaluates ticket vs DoR. Output schema (forced):

```json
{ "type":"object","required":["overall","criteria","missing_fields","ai_suggested_updates"],
  "properties":{
    "overall":{"enum":["PASS","FAIL"]},
    "criteria":{"type":"array","items":{"type":"object","required":["name","status","reason"],
       "properties":{"name":{"type":"string"},"status":{"enum":["PASS","FAIL"]},"reason":{"type":"string"}}}},
    "missing_fields":{"type":"array","items":{"type":"string"}},
    "ai_suggested_updates":{"type":"object","additionalProperties":{"type":"string"}} } }
```

Inputs interpolated: `{{dor_document}}`, `{{ticket_fields}}`.

## 2. Conversation — `ReplyEvaluation`

Interprets a human reply against outstanding gaps; decides resolution and composes the reply message.

```json
{ "type":"object","required":["resolved","remaining_gaps","field_updates","reply_message"],
  "properties":{
    "resolved":{"type":"boolean"},
    "remaining_gaps":{"type":"array","items":{"type":"string"}},
    "field_updates":{"type":"object","additionalProperties":{"type":"string"}},
    "reply_message":{"type":"string"} } }
```

Inputs: `{{failed_criteria}}`, `{{human_response}}`, `{{iteration_count}}`. `reply_message` is posted verbatim.
When `resolved`, `field_updates` must contain all changes needed — but see whitelist enforcement below.

## 3. Update — `FieldUpdatePayload`

Builds the Jira field body from the resolution. Called after `resolved == true`.

```json
{ "type":"object","required":["fields"],
  "properties":{"fields":{"type":"object","additionalProperties":true}} }
```

Inputs: `{{ai_editable_fields}}`, `{{field_updates}}`, `{{ticket_fields}}`.

## Whitelist enforcement (D7 — programmatic, not prompt-trusted)

Before any write, `TicketUpdateExecutor` intersects the proposed `fields`/`field_updates` keys with the configured
`ai_editable_fields`; any key not in the whitelist is dropped and logged. This holds even if the model returns a
non-whitelisted field (FR-021, SC-006). The update prompt is *also* given the whitelist as guidance.

## Dry-run

When `run.dry_run` is true, the executors run the AI calls and compute payloads but record a "would-do" audit
entry instead of calling `SetFieldsAsync`/`TransitionAsync`/message send (FR-032).
