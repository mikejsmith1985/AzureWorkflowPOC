using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Web.Integrations.ServiceNow;
using Microsoft.AspNetCore.Mvc;

namespace DBAIAzure.Web.Controllers;

/// <summary>
/// Receives inbound webhooks from external systems.
/// All endpoints validate a shared secret header to prevent unauthenticated intake.
/// </summary>
[ApiController]
[Route("api/webhook")]
public sealed class WebhookController : ControllerBase
{
    private readonly PipelineOrchestrator _orchestrator;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        PipelineOrchestrator orchestrator,
        IConfiguration config,
        ILogger<WebhookController> logger)
    {
        _orchestrator = orchestrator;
        _config       = config;
        _logger       = logger;
    }

    /// <summary>
    /// Receives a ServiceNow incident via outbound REST message.
    ///
    /// SNow configuration:
    ///   1. Create a REST Message in System Web Services > Outbound > REST Message
    ///   2. Set Endpoint to https://your-server/api/webhook/servicenow
    ///   3. Add HTTP Header: X-SNow-Secret = {the value in appsettings.json WebhookSecrets:ServiceNow}
    ///   4. Set the JSON body to map incident fields to the ServiceNowWebhookPayload schema
    ///   5. Trigger via Business Rule or Flow Designer on incident insert/update
    /// </summary>
    [HttpPost("servicenow")]
    public IActionResult ReceiveServiceNow([FromBody] ServiceNowWebhookPayload payload)
    {
        if (!ValidateSecret("WebhookSecrets:ServiceNow", "X-SNow-Secret"))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(payload.ShortDescription))
        {
            _logger.LogWarning("Rejected SNow webhook - missing short_description");
            return BadRequest(new { error = "short_description is required" });
        }

        var ticket = ServiceNowMapper.ToTicketState(payload);
        var runId  = _orchestrator.StartRun(ticket);

        _logger.LogInformation(
            "SNow incident {SnowNumber} accepted as run {RunId}", payload.Number, runId);

        return Accepted(new { runId, ticketId = ticket.TicketId });
    }

    private bool ValidateSecret(string configKey, string headerName) =>
        WebhookSecretValidator.Validate(_config, Request.Headers, _logger, configKey, headerName);
}
