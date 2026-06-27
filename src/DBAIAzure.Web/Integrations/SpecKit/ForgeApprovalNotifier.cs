// IPhaseApprovalNotifier implementation: POSTs the approval card to the Forge decision-card endpoint.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Web.Integrations.SpecKit;

/// <summary>
/// Pushes the validation summary + gaps + portal link to the Forge Terminal decision card when a run
/// pauses for review. Fire-and-forget and failure-tolerant (mirrors <c>MessagingHitlNotifier</c>): a
/// missed push must never block the run from entering its awaiting-approval state.
/// </summary>
public sealed class ForgeApprovalNotifier : IPhaseApprovalNotifier
{
    private readonly HttpClient _http;
    private readonly ILogger<ForgeApprovalNotifier> _logger;

    public ForgeApprovalNotifier(IHttpClientFactory httpClientFactory, ILogger<ForgeApprovalNotifier> logger)
    {
        _http = httpClientFactory.CreateClient(nameof(ForgeApprovalNotifier));
        _logger = logger;
    }

    public async Task NotifyAsync(
        string runId,
        string featureKey,
        SpecKitPhase phase,
        string summary,
        IReadOnlyList<PhaseValidationGap> gaps,
        string portalUrl,
        CancellationToken cancellationToken = default)
    {
        // No base address configured → the push is disabled; quietly skip (failure-tolerant).
        if (_http.BaseAddress is null)
        {
            _logger.LogDebug("Decision-card URL not configured — skipping approval push for run {RunId}", runId);
            return;
        }

        var payload = new
        {
            run_id = runId,
            feature_key = featureKey,
            phase = phase.ToString().ToLowerInvariant(),
            summary,
            gaps = gaps.Select(gap => new { label = gap.Label, description = gap.Description }).ToArray(),
            portal_url = portalUrl,
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync(string.Empty, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning(
                    "Decision-card push returned {StatusCode} for run {RunId}", response.StatusCode, runId);
            else
                _logger.LogInformation("Approval card pushed for run {RunId}", runId);
        }
        catch (Exception ex)
        {
            // Non-blocking: log and continue — the run still pauses and can be approved via the portal.
            _logger.LogError(ex, "Failed to push approval card for run {RunId}", runId);
        }
    }
}
