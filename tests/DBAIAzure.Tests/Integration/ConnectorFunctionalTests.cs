// Live round-trip tests — require real credentials via environment variables (T026, US1).
// Run with: dotnet test --filter "Category=Integration"
// Skipped automatically in standard CI: dotnet test --filter "Category!=Integration"
using DBAIAzure.Core.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DBAIAzure.Tests.Integration;

/// <summary>
/// Functional integration tests — one per connector — that make genuine network calls against the
/// real external services. Each test skips automatically when its required environment variable is
/// absent so the standard unit-test run is never blocked.
///
/// Required environment variables:
///   SNOW_INSTANCE_URL     — e.g. https://company.service-now.com
///   SNOW_USERNAME
///   SNOW_PASSWORD
///   ADO_ORG_URL           — e.g. https://dev.azure.com/myorg
///   ADO_PROJECT
///   ADO_PAT
///   ANTHROPIC_API_KEY
///   ANTHROPIC_MODEL       — e.g. claude-sonnet-4-6
///   TEAMS_WEBHOOK_URL
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConnectorFunctionalTests
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(35) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ── ServiceNow ────────────────────────────────────────────────────────

    [Fact]
    public async Task ServiceNow_BasicAuth_CanReadSysProperties()
    {
        var instanceUrl = Env("SNOW_INSTANCE_URL");
        var username    = Env("SNOW_USERNAME");
        var password    = Env("SNOW_PASSWORD");

        if (instanceUrl is null || username is null || password is null)
        {
            // Skip when credentials are not present in this environment.
            return;
        }

        var requestUrl = $"{instanceUrl.TrimEnd('/')}/api/now/table/sys_properties?sysparm_limit=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

        var response = await HttpClient.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode,
            $"ServiceNow returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    // ── Azure DevOps ──────────────────────────────────────────────────────

    [Fact]
    public async Task AzureDevOps_BasicAuthPat_CanReadProject()
    {
        var orgUrl  = Env("ADO_ORG_URL");
        var project = Env("ADO_PROJECT");
        var pat     = Env("ADO_PAT");

        if (orgUrl is null || project is null || pat is null)
            return;

        var requestUrl = $"{orgUrl.TrimEnd('/')}/_apis/projects/{Uri.EscapeDataString(project)}?api-version=7.1";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat}")));

        var response = await HttpClient.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode,
            $"Azure DevOps returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    // ── LLM (Anthropic) ───────────────────────────────────────────────────

    [Fact]
    public async Task Anthropic_WithApiKey_CanCompleteMinimalInference()
    {
        var apiKey = Env("ANTHROPIC_API_KEY");
        var model  = Env("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6";

        if (apiKey is null)
            return;

        var body = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 5,
            messages   = new[] { new { role = "user", content = "Respond with the word READY." } },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await HttpClient.SendAsync(request);
        var json     = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Anthropic returned {(int)response.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var content   = doc.RootElement
                           .GetProperty("content")[0]
                           .GetProperty("text")
                           .GetString();
        Assert.False(string.IsNullOrWhiteSpace(content), "LLM returned an empty response body.");
    }

    // ── Teams ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Teams_WebhookUrl_AcceptsAdaptiveCard()
    {
        var webhookUrl = Env("TEAMS_WEBHOOK_URL");

        if (webhookUrl is null)
            return;

        var card = JsonSerializer.Serialize(new
        {
            type  = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content     = new
                    {
                        type    = "AdaptiveCard",
                        version = "1.3",
                        body    = new[] { new { type = "TextBlock", text = "Integration test — AzureWorkflowPOC connector verification." } },
                    },
                },
            },
        });

        using var content  = new StringContent(card, Encoding.UTF8, "application/json");
        var response       = await HttpClient.PostAsync(webhookUrl, content);
        var responseBody   = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode,
            $"Teams webhook returned {(int)response.StatusCode}: {responseBody}");
        // Teams returns "1" in the body when the card is accepted.
        Assert.Equal("1", responseBody.Trim());
    }

    // ── Messaging: Slack webhook ──────────────────────────────────────────

    [Fact]
    public async Task Slack_Webhook_AcceptsTextMessage()
    {
        var webhookUrl = Env("SLACK_WEBHOOK_URL");
        if (webhookUrl is null)
            return;

        var profile = new DBAIAzure.Connectors.Messaging.SlackWebhookProfile();
        using var content = new StringContent(
            profile.BuildBody("Integration test — AzureWorkflowPOC Messaging connector (Slack)."),
            Encoding.UTF8, "application/json");

        var response = await HttpClient.PostAsync(webhookUrl, content);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.True(profile.IsSuccess((int)response.StatusCode, body),
            $"Slack returned {(int)response.StatusCode}: {body}");
    }

    // ── Messaging: Discord webhook ────────────────────────────────────────

    [Fact]
    public async Task Discord_Webhook_AcceptsContentMessage()
    {
        var webhookUrl = Env("DISCORD_WEBHOOK_URL");
        if (webhookUrl is null)
            return;

        var profile = new DBAIAzure.Connectors.Messaging.DiscordWebhookProfile();
        using var content = new StringContent(
            profile.BuildBody("Integration test — AzureWorkflowPOC Messaging connector (Discord)."),
            Encoding.UTF8, "application/json");

        var response = await HttpClient.PostAsync(webhookUrl, content);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.True(profile.IsSuccess((int)response.StatusCode, body),
            $"Discord returned {(int)response.StatusCode}: {body}");
    }

    // ── Messaging: MCP send-message tool ──────────────────────────────────

    [Fact]
    public async Task Mcp_SendMessageTool_DeliversMessage()
    {
        var serverUrl = Env("MCP_SERVER_URL");
        var toolName  = Env("MCP_TOOL_NAME");
        var target    = Env("MCP_TARGET");
        if (serverUrl is null || toolName is null || target is null)
            return;

        var gateway = new DBAIAzure.Connectors.Messaging.McpMessageGateway();
        var result  = await gateway.SendAsync(new DBAIAzure.Connectors.Messaging.McpSendRequest(
            serverUrl, toolName, Env("MCP_ARGUMENT_TEMPLATE"), target,
            "Integration test — AzureWorkflowPOC Messaging connector (MCP).", Env("MCP_AUTH_TOKEN")));

        Assert.True(result.IsSuccess, result.Message);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
}
