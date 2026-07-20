// Pure parsing + HMAC validation for inbound Jira webhooks (spec-021 trigger). Extracted from the controller so
// the security and filtering logic is unit-testable without an HttpContext.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Web.Integrations.Jira;

/// <summary>The trigger-relevant fields extracted from a Jira webhook payload.</summary>
public sealed record JiraTriggerInfo(string IssueKey, string ProjectKey, string IssueType, string WebhookEvent);

/// <summary>
/// Validates a Jira webhook's HMAC signature, parses its payload, and decides whether the configured scope
/// (project / issue types / created event) should trigger the DoR workflow. All methods are pure.
/// </summary>
public static class JiraWebhookParser
{
    private const string CreatedEventFragment = "issue_created";

    /// <summary>
    /// True when <paramref name="signatureHeader"/> (an <c>X-Hub-Signature</c> value, optionally prefixed with
    /// <c>sha256=</c>) is a valid HMAC-SHA256 of the raw body under <paramref name="secret"/>. A missing secret
    /// or signature returns false (secure default — an unverifiable request is rejected).
    /// </summary>
    public static bool IsSignatureValid(string rawBody, string? signatureHeader, string? secret)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signatureHeader))
            return false;

        var provided = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeader["sha256=".Length..]
            : signatureHeader;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(provided.ToLowerInvariant()));
    }

    /// <summary>Parses the issue key, project, issue type, and event; null when the payload is not a usable issue event.</summary>
    public static JiraTriggerInfo? Parse(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var webhookEvent = root.TryGetProperty("webhookEvent", out var e) ? e.GetString() ?? "" : "";

            if (!root.TryGetProperty("issue", out var issue) || issue.ValueKind != JsonValueKind.Object)
                return null;

            var key = issue.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(key))
                return null;

            var projectKey = "";
            var issueType = "";
            if (issue.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            {
                if (fields.TryGetProperty("project", out var project) && project.TryGetProperty("key", out var pk))
                    projectKey = pk.GetString() ?? "";
                if (fields.TryGetProperty("issuetype", out var it) && it.TryGetProperty("name", out var itn))
                    issueType = itn.GetString() ?? "";
            }

            return new JiraTriggerInfo(key, projectKey, issueType, webhookEvent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True when the event is an issue-created within the configured project(s) and issue type(s).</summary>
    public static bool ShouldProcess(JiraTriggerInfo info, DorJiraConfig jira)
    {
        if (!info.WebhookEvent.Contains(CreatedEventFragment, StringComparison.OrdinalIgnoreCase))
            return false;
        if (jira.ProjectKeys.Count > 0 && !jira.ProjectKeys.Contains(info.ProjectKey, StringComparer.OrdinalIgnoreCase))
            return false;
        if (jira.IssueTypes.Count > 0 && !jira.IssueTypes.Contains(info.IssueType, StringComparer.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
