// Posts HITL notification cards to Teams. Webhook URL is resolved from DB at each send (FR-014).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Web.Integrations.Teams;

/// <summary>
/// Posts an Adaptive Card-style notification to a Microsoft Teams channel via a webhook URL.
/// The URL is resolved from <see cref="IConnectorConfigRepository"/> at each call (hot-reload,
/// FR-014); falls back to the named <see cref="HttpClient"/> base address (IConfiguration) when
/// no DB-stored URL is available, so the existing setup path continues to work.
/// </summary>
public sealed class TeamsHitlNotifier : IHitlNotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectorConfigRepository? _configRepo;
    private readonly ILogger<TeamsHitlNotifier> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TeamsHitlNotifier(
        IHttpClientFactory httpClientFactory,
        ILogger<TeamsHitlNotifier> logger,
        IConnectorConfigRepository? configRepo = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
        _configRepo        = configRepo;
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
            string? dbWebhookUrl = null;

            if (_configRepo is not null)
            {
                var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.Messaging, cancellationToken);
                if (secretsJson is not null)
                {
                    var secrets = JsonSerializer.Deserialize<TeamsSecrets>(secretsJson, JsonOptions);
                    dbWebhookUrl = secrets?.WebhookUrl;
                }
            }

            HttpResponseMessage response;

            if (!string.IsNullOrEmpty(dbWebhookUrl))
            {
                // Hot-reload path: use the DB-stored URL on a plain unnamed client (no base address).
                var http = _httpClientFactory.CreateClient();
                response = await http.PostAsync(dbWebhookUrl, content, cancellationToken);
            }
            else
            {
                // Legacy path: use the named client whose base address was set from IConfiguration.
                var http = _httpClientFactory.CreateClient(nameof(TeamsHitlNotifier));
                response = await http.PostAsync(string.Empty, content, cancellationToken);
            }

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
            // Non-blocking: log and continue — the pipeline must not fail because of a missed ping.
            _logger.LogError(ex, "Failed to send Teams HITL notification for run {RunId}", runId);
        }
    }

    private sealed record TeamsSecrets(string? WebhookUrl);
}
