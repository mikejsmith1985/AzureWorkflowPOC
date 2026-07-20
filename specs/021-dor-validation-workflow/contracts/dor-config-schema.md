# Contract: DoR Workflow Configuration Schema

Stored as one non-secret JSON blob under `ConnectorType.DorWorkflow` via `IConnectorConfigRepository`; secrets by
reference in the encrypted-secret store. Resolved **per run** by `IDorConfigResolver.ResolveActiveAsync(ct)`
(hot-reload, FR-025). Unknown keys ignored; missing keys fall back to documented defaults.

## Resolver interface

```csharp
public interface IDorConfigResolver
{
    // Reads + parses the active DorWorkflow row per call; decrypts secrets server-side. Never throws
    // (best-effort) — returns DorWorkflowConfig.Unconfigured when absent/incomplete.
    Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default);
}
```

## JSON shape (non-secret)

```json
{
  "jira": {
    "base_url": "https://org.atlassian.net",
    "account_email": "bot@org.com",
    "project_keys": ["SBRO"],
    "issue_types": ["Story", "Bug"],
    "watch_fields": ["summary", "description", "acceptance_criteria", "customfield_10016"],
    "field_labels": { "customfield_10016": "Story Points", "acceptance_criteria": "Acceptance Criteria" },
    "ai_editable_fields": ["description", "acceptance_criteria"],
    "ready_transition_id": "31",
    "ready_status": "Ready to Work",
    "manual_label": "dor-manual-required"
  },
  "dor": { "source_type": "url", "source_uri": "https://.../dor", "inline_markdown": null,
           "cache_ttl_minutes": 15, "format": "markdown" },
  "ai": { "provider": "anthropic", "model": "claude-sonnet-5",
          "review_prompt_template": "...", "conversation_prompt_template": "...", "update_prompt_template": "...",
          "temperature": 0.1, "max_tokens": 2000 },
  "comms": {
    "primary": { "type": "slack", "channel_id": "#dor-review", "mention_users": [],
                 "reply_timeout_minutes": 240, "max_iterations": 3 },
    "escalation": { "type": "slack", "channel_id": "#dor-escalation", "mention_users": [],
                    "reply_timeout_minutes": 120, "max_iterations": 2 },
    "success": { "enabled": true, "channel_id": "#dor-passed" },
    "ignore_user_ids": []
  },
  "sla": { "primary_sla_hours": 24, "escalation_sla_hours": 8, "clock_type": "business_hours",
           "business_hours": { "timezone": "America/Chicago", "start": "08:00", "end": "17:00",
                               "working_days": [1,2,3,4,5] } },
  "audit": { "store_type": "jira_comment", "log_ai_responses": true,
             "jira_comment_on_pass": true, "jira_comment_on_fail": true, "jira_comment_on_escalation": true },
  "run": { "dry_run": false }
}
```

## Secrets (encrypted blob, resolved by reference — never in the JSON above)

`jira.api_token`, `jira.webhook_secret`, `comms.token` (Slack), `ai.api_key`. Each is named; values live only in
the encrypted-secret store and are decrypted server-side (Article IX, FR-026). The AI key MUST NOT be deployed to
the cloud image (existing FR-004 guard).

## Validation

- `dor.source_type == inline` ⇒ `inline_markdown` required; `== url` ⇒ `source_uri` required.
- `sla.clock_type == business_hours` ⇒ `business_hours.*` required; `working_days` non-empty (Mon=1..Sun=7).
- `ai_editable_fields ⊆ watch_fields` recommended (warn if not); enforced as the write whitelist (D7).
- `ready_transition_id` required for the pass/resolution transition.
- Health: `DorWorkflowTester` (on `IConnectorHealthChecker`) verifies Jira reachability + transition id exists,
  Slack channel reachable, DoR document loads, and AI key valid.
