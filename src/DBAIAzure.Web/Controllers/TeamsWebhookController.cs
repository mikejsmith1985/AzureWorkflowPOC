// Inbound Teams approval webhook — receives Adaptive Card action payloads and closes the approval loop (FR-19.3, FR-20.2).

using DBAIAzure.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DBAIAzure.Web.Controllers;

/// <summary>
/// Receives the POST from a Teams Adaptive Card "Approve / Reject" button and routes
/// the decision to the waiting <see cref="IWorkflowExecutionOrchestrator"/> via
/// <c>SubmitApproval(runId, approved)</c>. Authentication is validated by Microsoft JWT
/// before this controller is invoked — unauthenticated requests are rejected with 401
/// at the middleware level (FR-19.3).
/// </summary>
[ApiController]
[Route("api/teams/approval")]
public sealed class TeamsWebhookController : ControllerBase
{
    private readonly IWorkflowExecutionOrchestrator _orchestrator;
    private readonly ILogger<TeamsWebhookController> _logger;

    public TeamsWebhookController(
        IWorkflowExecutionOrchestrator orchestrator,
        ILogger<TeamsWebhookController> logger)
    {
        _orchestrator = orchestrator;
        _logger       = logger;
    }

    /// <summary>
    /// Receives an approval decision from a Teams Adaptive Card action.
    /// Body: <c>{ "runId": "...", "decision": "approve" | "reject" }</c>
    /// </summary>
    [HttpPost]
    public IActionResult ReceiveApproval([FromBody] JsonElement body)
    {
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
