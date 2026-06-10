using DBAIAzure.Core.Interfaces;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Web.Integrations.Teams;

/// <summary>
/// Posts an Adaptive Card-style notification to a Microsoft Teams channel via a
/// Power Automate HTTP trigger URL when the pipeline pauses for human input.
///
/// Setup (one-time, per user):
///   1. In Power Automate, create a new Flow: Trigger = "When an HTTP request is received"
///   2. Add action: "Post an Adaptive Card to a Teams chat or channel"
///   3. In the Adaptive Card body, reference the JSON fields this notifier sends
///   4. Save the Flow, copy the HTTP POST URL, add it to appsettings.Development.json
///
/// The card includes a "Respond in Portal" button deep-linking to the run detail page.
/// The PO answers in the web UI — no bot registration or Graph API required.
/// </summary>
public sealed class TeamsHitlNotifier : IHitlNotifier
{
    private readonly HttpClient _http;
    private readonly ILogger<TeamsHitlNotifier> _logger;

    public TeamsHitlNotifier(IHttpClientFactory httpClientFactory, ILogger<TeamsHitlNotifier> logger)
    {
        _http   = httpClientFactory.CreateClient(nameof(TeamsHitlNotifier));
        _logger = logger;
    }

    public async Task NotifyAsync(
        string runId,
        string ticketId,
        string title,
        IReadOnlyList<string> questions,
        string portalUrl,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            runId,
            ticketId,
            title,
            questions    = questions.ToArray(),
            portalUrl,
            questionText = string.Join("\n", questions.Select((q, i) => $"{i + 1}. {q}")),
            timestamp    = DateTimeOffset.UtcNow.ToString("o"),
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync(string.Empty, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Teams notification returned {StatusCode} for run {RunId}",
                    response.StatusCode, runId);
            }
            else
            {
                _logger.LogInformation("Teams HITL notification sent for run {RunId}", runId);
            }
        }
        catch (Exception ex)
        {
            // Non-blocking: log and continue — pipeline must not fail because of a missed ping
            _logger.LogError(ex, "Failed to send Teams HITL notification for run {RunId}", runId);
        }
    }
}
