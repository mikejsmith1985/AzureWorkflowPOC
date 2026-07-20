// Inbound Jira webhook endpoint (spec-021 trigger): validates the HMAC signature, filters to the configured
// scope, and starts the DoR workflow for a newly-created ticket. A thin wrapper over the pure JiraWebhookParser.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Web.Integrations.Jira;
using Microsoft.AspNetCore.Mvc;

namespace DBAIAzure.Web.Controllers;

/// <summary>Receives Jira <c>issue_created</c> webhooks and triggers the DoR validation workflow.</summary>
[ApiController]
[Route("webhooks/jira")]
public sealed class JiraWebhookController : ControllerBase
{
    private readonly DorWorkflowOrchestrator _orchestrator;
    private readonly IDorConfigResolver _configResolver;
    private readonly ILogger<JiraWebhookController> _logger;

    public JiraWebhookController(
        DorWorkflowOrchestrator orchestrator, IDorConfigResolver configResolver, ILogger<JiraWebhookController> logger)
    {
        _orchestrator = orchestrator;
        _configResolver = configResolver;
        _logger = logger;
    }

    /// <summary>Handles an inbound Jira webhook: HMAC-validate → parse → filter → start the workflow.</summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var secrets = await _configResolver.ResolveSecretsAsync(cancellationToken);
        var signature = Request.Headers["X-Hub-Signature"].FirstOrDefault();
        if (!JiraWebhookParser.IsSignatureValid(body, signature, secrets.JiraWebhookSecret))
        {
            _logger.LogWarning("Rejected a Jira webhook with an invalid or missing HMAC signature.");
            return Unauthorized();
        }

        var info = JiraWebhookParser.Parse(body);
        if (info is null)
            return BadRequest("Unrecognized Jira webhook payload.");

        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        if (!JiraWebhookParser.ShouldProcess(info, config.Jira))
            return Ok(new { ignored = true, reason = "outside the configured project/issue-type scope" });

        await _orchestrator.StartAsync(info.IssueKey, cancellationToken);
        return Accepted();
    }
}
