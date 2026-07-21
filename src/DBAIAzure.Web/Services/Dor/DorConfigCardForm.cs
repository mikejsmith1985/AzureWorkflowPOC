// View-model + pure serialization for the DoR Validation Workflow configuration card (spec-021 US6). Splits the
// single six-namespace config blob into first-class DoR-document fields (so operators paste raw markdown, not
// escaped JSON) and a raw JSON block for the remaining namespaces, then merges them back on save.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// The editable form behind the DoR workflow config card. The DoR document lives in dedicated fields because a
/// markdown document does not belong inside a JSON text box; every other namespace (jira, ai, comms, sla, audit)
/// stays in <see cref="OtherConfigJson"/> so the card never has to hand-render forty nested fields yet loses
/// nothing. <see cref="Parse"/> and <see cref="ToConfigJson"/> are pure so the round-trip is unit-testable, and
/// the emitted JSON is snake_case to match exactly what <c>DorConfigResolver</c> reads back.
/// </summary>
public sealed class DorConfigCardForm
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // The DoR document source type this card understands; the workflow also supports "url".
    private const string InlineSourceType = "inline";
    private const string UrlSourceType = "url";
    private const int DefaultCacheTtlMinutes = 15;

    /// <summary>Selected DoR document source — <c>inline</c> or <c>url</c>.</summary>
    public string SourceType { get; set; } = InlineSourceType;

    /// <summary>The DoR document as raw markdown (used when <see cref="SourceType"/> is <c>inline</c>).</summary>
    public string InlineMarkdown { get; set; } = string.Empty;

    /// <summary>The published DoR document URL (used when <see cref="SourceType"/> is <c>url</c>).</summary>
    public string SourceUri { get; set; } = string.Empty;

    /// <summary>Minutes a fetched URL document is cached before re-fetching; 0 means always fresh.</summary>
    public int CacheTtlMinutes { get; set; } = DefaultCacheTtlMinutes;

    /// <summary>When true the workflow logs intended Jira writes / messages without performing them (FR-032).</summary>
    public bool DryRun { get; set; }

    /// <summary>The remaining namespaces (jira, ai, comms, sla, audit) as editable snake_case JSON.</summary>
    public string OtherConfigJson { get; set; } = string.Empty;

    /// <summary>
    /// Splits a stored six-namespace config blob into this form: the <c>dor</c> and <c>run</c> namespaces become
    /// first-class fields, everything else becomes the editable JSON block. A blank blob yields an empty form; a
    /// malformed blob is surfaced verbatim in <see cref="OtherConfigJson"/> so the operator can repair it.
    /// </summary>
    public static DorConfigCardForm Parse(string? fullConfigJson)
    {
        var form = new DorConfigCardForm();
        if (string.IsNullOrWhiteSpace(fullConfigJson))
            return form;

        JsonObject root;
        try
        {
            root = JsonNode.Parse(fullConfigJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            // Show the unparseable text back to the operator rather than silently dropping it.
            form.OtherConfigJson = fullConfigJson;
            return form;
        }

        form.ExtractDorNamespace(root);
        form.ExtractRunNamespace(root);
        form.OtherConfigJson = root.Count == 0 ? string.Empty : root.ToJsonString(Indented);
        return form;
    }

    /// <summary>Pulls the <c>dor</c> namespace out of the blob into the dedicated document fields.</summary>
    private void ExtractDorNamespace(JsonObject root)
    {
        if (root["dor"] is not JsonObject dor)
            return;

        SourceType = (string?)dor["source_type"] ?? InlineSourceType;
        InlineMarkdown = (string?)dor["inline_markdown"] ?? string.Empty;
        SourceUri = (string?)dor["source_uri"] ?? string.Empty;
        if (dor["cache_ttl_minutes"] is JsonValue ttl && ttl.TryGetValue<int>(out var ttlMinutes))
            CacheTtlMinutes = ttlMinutes;
        root.Remove("dor");
    }

    /// <summary>Pulls the <c>run</c> namespace out of the blob into the dry-run toggle.</summary>
    private void ExtractRunNamespace(JsonObject root)
    {
        if (root["run"] is JsonObject run
            && run["dry_run"] is JsonValue dryRun && dryRun.TryGetValue<bool>(out var isDryRun))
        {
            DryRun = isDryRun;
        }
        root.Remove("run");
    }

    /// <summary>
    /// Merges the dedicated DoR-document/dry-run fields back into the JSON block to produce the complete
    /// snake_case config blob for <c>IConnectorConfigRepository.SaveAsync</c>. Throws <see cref="FormatException"/>
    /// when the JSON block is not a valid JSON object so the card can show an actionable save error.
    /// </summary>
    public string ToConfigJson()
    {
        var root = ParseOtherConfig();
        root["dor"] = BuildDorNamespace();
        root["run"] = new JsonObject { ["dry_run"] = DryRun };
        return root.ToJsonString(Indented);
    }

    /// <summary>Parses the editable JSON block, rejecting anything that is not a JSON object.</summary>
    private JsonObject ParseOtherConfig()
    {
        if (string.IsNullOrWhiteSpace(OtherConfigJson))
            return new JsonObject();

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(OtherConfigJson);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The workflow configuration is not valid JSON: {ex.Message}", ex);
        }

        return parsed as JsonObject
               ?? throw new FormatException("The workflow configuration must be a JSON object.");
    }

    /// <summary>Builds the <c>dor</c> namespace from the dedicated fields, including only the chosen source's field.</summary>
    private JsonObject BuildDorNamespace()
    {
        var dor = new JsonObject
        {
            ["source_type"] = SourceType,
            ["format"] = "markdown",
            ["cache_ttl_minutes"] = CacheTtlMinutes,
        };

        if (string.Equals(SourceType, UrlSourceType, StringComparison.OrdinalIgnoreCase))
            dor["source_uri"] = SourceUri;
        else
            dor["inline_markdown"] = InlineMarkdown;

        return dor;
    }

    /// <summary>
    /// Builds the encrypted-secret blob for the one secret the DoR workflow actually consumes: the Jira webhook
    /// HMAC secret (used to validate inbound Jira webhooks). Jira, Slack, and AI credentials are deliberately NOT
    /// collected here — the workflow reuses the Work-Tracker, Messaging, and LLM connectors for those. Returns
    /// null when blank so an existing secret is preserved (Article IX — a blank field never clears a stored one).
    /// </summary>
    public static string? BuildSecretsJson(string? jiraWebhookSecret)
    {
        if (string.IsNullOrWhiteSpace(jiraWebhookSecret))
            return null;
        return new JsonObject { ["jira_webhook_secret"] = jiraWebhookSecret }.ToJsonString();
    }

    /// <summary>
    /// Produces a ready-to-edit starter configuration: a sample inline DoR checklist plus sensible defaults for
    /// every other namespace, with dry-run ON so a freshly configured workflow never performs a live Jira write
    /// until the operator deliberately turns it off. The starter is a valid, health-check-passing baseline the
    /// operator then edits (project keys, transition id, channels) for their own Jira.
    /// </summary>
    public static DorConfigCardForm CreateStarter() => new()
    {
        SourceType = InlineSourceType,
        InlineMarkdown = SampleDorMarkdown,
        CacheTtlMinutes = DefaultCacheTtlMinutes,
        DryRun = true,
        OtherConfigJson = StarterOtherJson,
    };

    /// <summary>A short, editable Definition-of-Ready checklist used as the inline starter document.</summary>
    public const string SampleDorMarkdown = """
        # Definition of Ready

        A ticket is Ready to Work when all of the following are true:

        1. **Summary** — clearly states the desired outcome in one sentence.
        2. **Description** — explains the business context and the "why".
        3. **Acceptance Criteria** — at least one testable, unambiguous criterion.
        4. **Estimate** — a story-point or effort estimate is present.
        5. **Dependencies** — any blocking work or external dependency is named.
        """;

    /// <summary>The non-DoR namespaces of the starter config, snake_case, with placeholders the operator edits.
    /// Jira auth (base URL / email), the AI provider/model, and Slack/AI/Jira credentials are intentionally
    /// absent — the workflow reuses the Work-Tracker, LLM, and Messaging connectors, so only DoR-specific
    /// behaviour lives here.</summary>
    public const string StarterOtherJson = """
        {
          "jira": {
            "project_keys": ["SBRO"],
            "issue_types": ["Story"],
            "watch_fields": ["summary", "description", "acceptance_criteria"],
            "field_labels": { "acceptance_criteria": "Acceptance Criteria" },
            "ai_editable_fields": ["acceptance_criteria"],
            "ready_transition_id": "31",
            "ready_status": "Ready to Work",
            "manual_label": "dor-manual-required"
          },
          "ai": {
            "temperature": 0.1,
            "max_tokens": 2000
          },
          "comms": {
            "primary": { "type": "slack", "channel_id": "C0BDFFFJLRH", "reply_timeout_minutes": 240, "max_iterations": 3 },
            "escalation": { "type": "slack", "channel_id": "C0BDFFFJLRH", "reply_timeout_minutes": 120, "max_iterations": 2 },
            "success": { "enabled": true, "channel_id": "C0BDFFFJLRH" },
            "ignore_user_ids": []
          },
          "sla": {
            "primary_sla_hours": 24,
            "escalation_sla_hours": 8,
            "clock_type": "business_hours",
            "business_hours": { "timezone": "America/Chicago", "start": "08:00", "end": "17:00", "working_days": [1, 2, 3, 4, 5] }
          },
          "audit": {
            "store_type": "jira_comment",
            "log_ai_responses": true,
            "jira_comment_on_pass": true,
            "jira_comment_on_fail": true,
            "jira_comment_on_escalation": true
          }
        }
        """;
}
