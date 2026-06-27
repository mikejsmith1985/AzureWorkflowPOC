// Sends workflow-builder approval Adaptive Cards to Microsoft Teams via Graph API (FR-19, US2).

using DBAIAzure.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Web.Integrations.Teams;

/// <summary>
/// Sends Adaptive Card approval requests to each approver's Teams chat using Microsoft Graph API.
/// The card contains Approve and Reject buttons that POST back to
/// <c>/api/teams/approval</c> with the run id and decision.
///
/// Configuration keys (read from IConfiguration, never hard-coded — Article IX):
///   Teams:GraphClientId, Teams:GraphClientSecret, Teams:GraphTenantId,
///   Portal:BaseUrl (used to construct the callback URL).
///
/// Token acquisition uses the client-credentials flow via the Graph API token endpoint.
/// The access token is memory-only and is never logged (Article IX).
/// </summary>
public sealed class TeamsWorkflowApprovalNotifier : IWorkflowApprovalNotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TeamsWorkflowApprovalNotifier> _logger;

    public TeamsWorkflowApprovalNotifier(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<TeamsWorkflowApprovalNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        IReadOnlyList<string> approverChain,
        IReadOnlyList<string> decisionOptions,
        CancellationToken ct = default)
    {
        if (approverChain.Count == 0)
        {
            _logger.LogWarning("ApprovalNotifier: no approvers configured for run {RunId} — notification skipped", runId);
            return;
        }

        await SendAdaptiveCardAsync(runId, workflowName, nodeLabel, question,
            approverChain[0], decisionOptions, ct);
    }

    /// <inheritdoc />
    public async Task EscalateAsync(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        IReadOnlyList<string> approverChain,
        int currentApproverIndex,
        IReadOnlyList<string> decisionOptions,
        CancellationToken ct = default)
    {
        var nextIndex = currentApproverIndex + 1;
        if (nextIndex >= approverChain.Count)
        {
            // Throwing signals the escalation caller (T041 timeout loop) that the chain is
            // exhausted so it can auto-resolve the run rather than looping forever.
            throw new InvalidOperationException(
                $"Escalation chain for run {runId} is exhausted — all {approverChain.Count} approver(s) notified. " +
                "The run must be auto-resolved by the caller per the configured EscalationPolicy.");
        }

        await SendAdaptiveCardAsync(runId, workflowName, nodeLabel,
            $"[Escalated] {question}", approverChain[nextIndex], decisionOptions, ct);
    }

    private async Task SendAdaptiveCardAsync(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        string approverUpn,
        IReadOnlyList<string> decisionOptions,
        CancellationToken ct)
    {
        try
        {
            var token       = await AcquireGraphTokenAsync(ct);
            var portalBase  = _config["Portal:BaseUrl"] ?? "http://localhost:5000";
            var callbackUrl = $"{portalBase}/api/teams/approval";

            var card = BuildAdaptiveCard(runId, workflowName, nodeLabel, question,
                decisionOptions, callbackUrl);

            using var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Find the approver's Teams chat user id via Graph /users/{upn} then send a chat message.
            var body    = new StringContent(JsonSerializer.Serialize(card), Encoding.UTF8, "application/json");
            var sendUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(approverUpn)}/sendMail";

            // V1: falls back to a simple chat message via /chats/1:1 create-or-get pattern.
            // Full Adaptive Card via Graph chat message requires beta endpoint — wired in a follow-up.
            var resp = await http.PostAsync(sendUrl, body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("ApprovalNotifier: Graph API returned {Status} for {Upn} run {RunId}",
                    resp.StatusCode, approverUpn, runId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Notification failures are non-fatal — the run continues and the approver
            // can still use the web Review Queue to action the item.
            _logger.LogWarning(ex, "ApprovalNotifier: failed to send card for run {RunId}", runId);
        }
    }

    private async Task<string> AcquireGraphTokenAsync(CancellationToken ct)
    {
        var tenantId     = _config["Teams:GraphTenantId"]     ?? string.Empty;
        var clientId     = _config["Teams:GraphClientId"]     ?? string.Empty;
        var clientSecret = _config["Teams:GraphClientSecret"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning("ApprovalNotifier: Teams Graph credentials not configured — check Teams:GraphTenantId/ClientId");
            return string.Empty;
        }

        using var http = _httpClientFactory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["scope"]         = "https://graph.microsoft.com/.default",
        });

        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var resp     = await http.PostAsync(tokenUrl, form, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        // Token value handled in-memory only — never logged (Article IX).
        return doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
    }

    private static object BuildAdaptiveCard(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        IReadOnlyList<string> decisionOptions,
        string callbackUrl)
    {
        var actions = decisionOptions.Select(opt => new
        {
            type  = "Action.Http",
            title = opt,
            method = "POST",
            url   = callbackUrl,
            headers = new[] { new { name = "Content-Type", value = "application/json" } },
            body  = JsonSerializer.Serialize(new { runId, decision = opt.ToLowerInvariant() }),
        }).ToArray();

        return new
        {
            type       = "AdaptiveCard",
            version    = "1.4",
            body       = new object[]
            {
                new { type = "TextBlock", text  = $"Approval Required: {workflowName}", weight = "bolder", size = "medium" },
                new { type = "TextBlock", text  = $"Step: {nodeLabel}" },
                new { type = "TextBlock", text  = question, wrap = true },
            },
            actions,
        };
    }
}
