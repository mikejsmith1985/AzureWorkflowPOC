// Inbound Spec Kit webhooks: the phase-complete signal and the approval-decision callback.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Web.Integrations.SpecKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DBAIAzure.Web.Controllers;

/// <summary>
/// Receives the Spec-Driven Development phase-complete signal and the reviewer's approval decision.
/// Both endpoints validate a shared secret header (mirrors the ServiceNow webhook pattern) so an
/// unauthorized signal is rejected before any run starts (FR-002).
/// </summary>
[ApiController]
[Route("api/webhook")]
public sealed class SpecKitWebhookController : ControllerBase
{
    private const string SecretConfigKey = "WebhookSecrets:SpecKit";
    private const string SecretHeaderName = "X-SpecKit-Secret";

    private readonly PhaseHandlerOrchestrator _orchestrator;
    private readonly SpecKitOptions _specKitOptions;
    private readonly IConfiguration _config;
    private readonly ILogger<SpecKitWebhookController> _logger;

    public SpecKitWebhookController(
        PhaseHandlerOrchestrator orchestrator,
        IOptions<SpecKitOptions> specKitOptions,
        IConfiguration config,
        ILogger<SpecKitWebhookController> logger)
    {
        _orchestrator = orchestrator;
        _specKitOptions = specKitOptions.Value;
        _config = config;
        _logger = logger;
    }

    /// <summary>Phase-complete signal → starts a phase-handler run (async; returns 202).</summary>
    [HttpPost("speckit-phase")]
    public IActionResult ReceivePhaseSignal([FromBody] PhaseSignalPayload payload)
    {
        if (!ValidateSecret()) return Unauthorized();

        if (string.IsNullOrWhiteSpace(payload.FeatureKey))
            return BadRequest(new { error = "feature_key is required" });
        if (string.IsNullOrWhiteSpace(payload.Phase))
            return BadRequest(new { error = "phase is required" });

        var runId = Guid.NewGuid().ToString("N")[..8];
        var initialState = SpecKitSignalMapper.ToInitialState(payload, runId, _specKitOptions.SpecsRoot);
        _orchestrator.StartRun(initialState);

        _logger.LogInformation(
            "Spec Kit phase '{Phase}' for '{FeatureKey}' accepted as run {RunId}",
            initialState.Phase, initialState.FeatureKey, runId);

        return Accepted(new { runId, featureKey = initialState.FeatureKey, phase = payload.Phase });
    }

    /// <summary>Approval callback → resumes the paused run with the decision.</summary>
    [HttpPost("speckit-approval")]
    public IActionResult ReceiveApproval([FromBody] ApprovalDecisionPayload payload)
    {
        if (!ValidateSecret()) return Unauthorized();

        if (string.IsNullOrWhiteSpace(payload.RunId))
            return BadRequest(new { error = "run_id is required" });
        if (payload.Approved is null)
            return BadRequest(new { error = "approved is required" });

        var decision = new ApprovalDecision
        {
            IsApproved = payload.Approved.Value,
            DecidedBy = payload.DecidedBy,
            Note = payload.Note,
        };

        var result = _orchestrator.SubmitApproval(payload.RunId, decision);
        if (result == PhaseHandlerOrchestrator.ApprovalResult.NotAwaiting)
            return NotFound(new { error = "no run awaiting approval for run_id" });

        var externalStatus = decision.IsApproved ? "accepted" : "rejected";
        return Ok(new { runId = payload.RunId, status = externalStatus });
    }

    /// <summary>Validates the shared secret header; rejects when missing/mismatched or unconfigured.</summary>
    private bool ValidateSecret()
    {
        var expectedSecret = _config[SecretConfigKey];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            _logger.LogWarning("Spec Kit webhook secret not configured — rejecting all requests");
            return false;
        }

        if (!Request.Headers.TryGetValue(SecretHeaderName, out var providedSecret) ||
            !string.Equals(expectedSecret, providedSecret.ToString(), StringComparison.Ordinal))
        {
            _logger.LogWarning("Invalid or missing {HeaderName} header", SecretHeaderName);
            return false;
        }

        return true;
    }
}
