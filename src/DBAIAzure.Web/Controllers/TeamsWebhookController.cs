// Inbound Teams approval webhook — receives Adaptive Card action payloads and closes the approval loop (FR-19.3, FR-20.2).

using DBAIAzure.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DBAIAzure.Web.Controllers;

/// <summary>
/// Receives the POST from a Teams Adaptive Card "Approve / Reject" button and routes
/// the decision to the waiting <see cref="IWorkflowExecutionOrchestrator"/> via
/// <c>SubmitApproval(runId, approved)</c>. Requests are guarded by a shared secret in
/// the X-Teams-Secret header — the same pattern used by all inbound webhook controllers
/// (FR-19.3). Configure the secret at WebhookSecrets:Teams in appsettings / user secrets.
/// </summary>
[ApiController]
[Route("api/teams/approval")]
public sealed class TeamsWebhookController : ControllerBase
{
    private const string SecretConfigKey  = "WebhookSecrets:Teams";
    private const string SecretHeaderName = "X-Teams-Secret";

    private readonly IWorkflowExecutionOrchestrator _orchestrator;
    private readonly IConfiguration _config;
    private readonly ILogger<TeamsWebhookController> _logger;

    public TeamsWebhookController(
        IWorkflowExecutionOrchestrator orchestrator,
        IConfiguration config,
        ILogger<TeamsWebhookController> logger)
    {
        _orchestrator = orchestrator;
        _config       = config;
        _logger       = logger;
    }

    /// <summary>
    /// Receives an approval decision from a Teams Adaptive Card action.
    /// Body: <c>{ "runId": "...", "decision": "approve" | "reject" }</c>
    /// The X-Teams-Secret header must match WebhookSecrets:Teams in configuration (T035).
    /// </summary>
    [HttpPost]
    public IActionResult ReceiveApproval([FromBody] JsonElement body)
    {
        if (!WebhookSecretValidator.Validate(_config, Request.Headers, _logger,
            configKey:  SecretConfigKey,
            headerName: SecretHeaderName))
        {
            return Unauthorized(new { error = "Invalid or missing webhook secret." });
        }

        if (!body.TryGetProperty("runId", out var runIdEl) ||
            runIdEl.ValueKind != JsonValueKind.String)
        {
            return BadRequest(new { error = "Missing or invalid runId" });
        }

        if (!body.TryGetProperty("decision", out var decisionEl) ||
            decisionEl.ValueKind != JsonValueKind.String)
        {
            return BadRequest(new { error = "Missing or invalid decision" });
        }

        var runId    = runIdEl.GetString()!;
        var decision = decisionEl.GetString()!;
        var approved = decision.Equals("approve", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("Teams approval received: runId={RunId} decision={Decision}", runId, decision);
        _orchestrator.SubmitApproval(runId, approved);

        // Teams expects a 200 with a bot-activity response (or empty) to dismiss the loading spinner.
        return Ok(new { status = "received" });
    }
}
