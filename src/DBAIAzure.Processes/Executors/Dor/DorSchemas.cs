// JSON schemas for the DoR workflow's forced-tool AI calls (spec-021). Paired with IStructuredCompletionService
// so the model returns schema-bound JSON — no free-text parsing (Article VII).
namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>The input schemas for the three DoR structured-completion tools (review, reply-eval, update).</summary>
public static class DorSchemas
{
    /// <summary>Schema for <c>DorReviewResult</c> — the review verdict.</summary>
    public const string ReviewSchema = """
        {
          "type": "object",
          "required": ["overall", "criteria", "missing_fields", "ai_suggested_updates"],
          "properties": {
            "overall": { "type": "string", "enum": ["PASS", "FAIL"] },
            "criteria": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["name", "status", "reason"],
                "properties": {
                  "name": { "type": "string" },
                  "status": { "type": "string", "enum": ["PASS", "FAIL"] },
                  "reason": { "type": "string" }
                }
              }
            },
            "missing_fields": { "type": "array", "items": { "type": "string" } },
            "ai_suggested_updates": { "type": "object", "additionalProperties": { "type": "string" } }
          }
        }
        """;

    /// <summary>Schema for <c>ReplyEvaluation</c> — the human-reply interpretation.</summary>
    public const string ReplyEvaluationSchema = """
        {
          "type": "object",
          "required": ["resolved", "remaining_gaps", "field_updates", "reply_message"],
          "properties": {
            "resolved": { "type": "boolean" },
            "remaining_gaps": { "type": "array", "items": { "type": "string" } },
            "field_updates": { "type": "object", "additionalProperties": { "type": "string" } },
            "reply_message": { "type": "string" }
          }
        }
        """;

    /// <summary>Schema for <c>FieldUpdatePayload</c> — the Jira field body.</summary>
    public const string FieldUpdateSchema = """
        {
          "type": "object",
          "required": ["fields"],
          "properties": {
            "fields": { "type": "object", "additionalProperties": { "type": "string" } }
          }
        }
        """;
}
