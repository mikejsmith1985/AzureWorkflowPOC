// Inbound development-spend ingest (spec-017): records a coding-agent session's AI cost against a ticket.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Web.Integrations.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace DBAIAzure.Web.Controllers;

/// <summary>
/// Receives development AI-usage from coding-agent sessions and appends it to the cost ledger as the
/// Development dimension, attributed to the ticket via its binding key. Secret-gated (mirrors the other
/// webhooks). A key that does not resolve is recorded as unattributed rather than dropped (FR-010).
/// </summary>
[ApiController]
[Route("api/telemetry")]
public sealed class TelemetryIngestController : ControllerBase
{
    private const string SecretConfigKey = "WebhookSecrets:Telemetry";
    private const string SecretHeaderName = "X-Telemetry-Secret";

    private readonly ICostLedger _ledger;
    private readonly IBindingWorkItemMap _bindingMap;
    private readonly ICostProjection _projection;
    private readonly IConfiguration _config;
    private readonly ILogger<TelemetryIngestController> _logger;

    public TelemetryIngestController(
        ICostLedger ledger,
        IBindingWorkItemMap bindingMap,
        ICostProjection projection,
        IConfiguration config,
        ILogger<TelemetryIngestController> logger)
    {
        _ledger = ledger;
        _bindingMap = bindingMap;
        _projection = projection;
        _config = config;
        _logger = logger;
    }

    /// <summary>Records one session's development AI spend (async; returns 202).</summary>
    [HttpPost("dev-usage")]
    public async Task<IActionResult> ReceiveDevUsage([FromBody] DevUsageIngestPayload payload)
    {
        if (!WebhookSecretValidator.Validate(_config, Request.Headers, _logger, SecretConfigKey, SecretHeaderName))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(payload.BindingKey))
            return BadRequest(new { error = "binding_key is required" });

        // Resolve the binding key locally (C1); a miss is recorded as unattributed, never dropped.
        var workItem = await _bindingMap.ResolveAsync(payload.BindingKey);

        // Prefer a supplied cost; otherwise re-price the tokens (one source of truth — ModelPricing).
        var cost = payload.CostUsd
            ?? ModelPricing.EstimateCostUsd(payload.Model, payload.InputTokens, payload.OutputTokens, payload.CacheReadTokens)
            ?? 0;

        await _ledger.AppendAsync(new CostLedgerEntry
        {
            Id = Guid.NewGuid(),
            BindingKey = payload.BindingKey,
            Dimension = CostDimension.Development,
            WorkItemId = workItem?.Value,
            ModelName = payload.Model,
            InputTokens = payload.InputTokens,
            OutputTokens = payload.OutputTokens,
            CacheReadTokens = payload.CacheReadTokens,
            CostUsd = cost,
            OccurredAt = payload.OccurredAt ?? DateTimeOffset.UtcNow,
            SourceId = payload.SessionId,
            IsUnattributed = workItem is null,
        });

        // Project the cumulative cost onto the resolved item through the active tracker (any tracker).
        if (workItem is { } resolved)
            await _projection.ProjectAsync(payload.BindingKey, resolved);

        return Accepted(new { bindingKey = payload.BindingKey, attributed = workItem is not null });
    }
}
