// Unit tests for MessageDelivery path selection — MCP-first / webhook fallback / not-configured (T009).
using System.Net;
using System.Text.Json;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Verifies the MCP-first-with-webhook-fallback selection rules (FR-005) and that delivery failures are
/// returned, never thrown (FR-010). The MCP path is wired in a later phase; here a configured MCP server
/// reports the MCP path without silently falling back to the webhook (Edge Cases / contract C4).
/// </summary>
public sealed class MessageDeliverySelectionTests
{
    private static IPlatformWebhookProfile[] Profiles() =>
        [new TeamsWebhookProfile(), new SlackWebhookProfile(), new DiscordWebhookProfile()];

    private static string PlatformJson(MessagingPlatform platform, string? mcpServerUrl = null) =>
        JsonSerializer.Serialize(new { platform = platform.ToString(), mcpServerUrl });

    [Fact]
    public async Task WebhookOnly_DeliversViaWebhook_WithPlatformBody()
    {
        var repo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = PlatformJson(MessagingPlatform.Slack),
            DecryptedSecretsJson = """{"webhookUrl":"https://hooks.example/abc"}""",
        };
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, "ok");
        var delivery = new MessageDelivery(repo, new SingleHandlerHttpClientFactory(handler), Profiles());

        var result = await delivery.SendAsync("deploy complete");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(DeliveryPath.Webhook, result.Path);
        Assert.Equal(MessagingPlatform.Slack, result.Platform);
        Assert.Equal("https://hooks.example/abc", handler.LastRequestUri!.ToString());
        Assert.Contains("\"text\":\"deploy complete\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task NeitherConfigured_ReportsNotConfigured()
    {
        var repo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = PlatformJson(MessagingPlatform.Teams),
            DecryptedSecretsJson = null,
        };
        var delivery = new MessageDelivery(repo,
            new SingleHandlerHttpClientFactory(new CapturingHttpHandler(HttpStatusCode.OK, "")), Profiles());

        var result = await delivery.SendAsync("hi");

        Assert.False(result.IsSuccess);
        Assert.Equal(DeliveryPath.NotConfigured, result.Path);
    }

    [Fact]
    public async Task McpServerConfigured_SelectsMcpPath_WithoutWebhookFallback()
    {
        var repo = new StubConnectorConfigRepository
        {
            // MCP url present AND a webhook present — selection must prefer MCP, not fall back.
            NonSecretConfigJson = PlatformJson(MessagingPlatform.Slack, mcpServerUrl: "https://mcp.example/sse"),
            DecryptedSecretsJson = """{"webhookUrl":"https://hooks.example/abc"}""",
        };
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, "ok");
        var delivery = new MessageDelivery(repo, new SingleHandlerHttpClientFactory(handler), Profiles());

        var result = await delivery.SendAsync("hi");

        Assert.Equal(DeliveryPath.Mcp, result.Path);
        Assert.Null(handler.LastRequestBody);   // no webhook POST happened
    }

    [Fact]
    public async Task McpConfigured_WithGateway_DeliversViaMcp_PassingTemplateAndAuth()
    {
        var repo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = JsonSerializer.Serialize(new
            {
                platform = nameof(MessagingPlatform.Slack),
                mcpServerUrl = "https://mcp.example/sse",
                mcpToolName = "slack_post_message",
                mcpArgumentTemplate = """{"channel":"{{target}}","text":"{{message}}"}""",
                target = "C999",
            }),
            DecryptedSecretsJson = """{"mcpAuthToken":"secret-token","webhookUrl":"https://hooks.example/abc"}""",
        };
        var gateway = new FakeMcpMessageGateway(succeeds: true);
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, "ok");
        var delivery = new MessageDelivery(repo, new SingleHandlerHttpClientFactory(handler), Profiles(), gateway);

        var result = await delivery.SendAsync("hello mcp");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(DeliveryPath.Mcp, result.Path);
        Assert.Null(handler.LastRequestBody);                 // webhook never used
        Assert.NotNull(gateway.LastRequest);
        Assert.Equal("https://mcp.example/sse", gateway.LastRequest!.ServerUrl);
        Assert.Equal("slack_post_message", gateway.LastRequest.ToolName);
        Assert.Equal("C999", gateway.LastRequest.Target);
        Assert.Equal("hello mcp", gateway.LastRequest.Message);
        Assert.Equal("secret-token", gateway.LastRequest.AuthToken);
    }

    [Fact]
    public async Task McpGatewayFailure_StaysOnMcpPath_DoesNotFallBackOrThrow()
    {
        var repo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = JsonSerializer.Serialize(new
            {
                platform = nameof(MessagingPlatform.Teams),
                mcpServerUrl = "https://mcp.example/sse",
                mcpToolName = "post",
            }),
            DecryptedSecretsJson = """{"webhookUrl":"https://hooks.example/abc"}""",
        };
        var gateway = new FakeMcpMessageGateway(succeeds: false, message: "could not reach MCP server");
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, "1");
        var delivery = new MessageDelivery(repo, new SingleHandlerHttpClientFactory(handler), Profiles(), gateway);

        var result = await delivery.SendAsync("hi");   // must not throw

        Assert.False(result.IsSuccess);
        Assert.Equal(DeliveryPath.Mcp, result.Path);
        Assert.Null(handler.LastRequestBody);          // no silent webhook fallback
    }

    [Fact]
    public async Task WebhookTransportFailure_ReturnsFailure_DoesNotThrow()
    {
        var repo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = PlatformJson(MessagingPlatform.Discord),
            DecryptedSecretsJson = """{"webhookUrl":"https://discord.example/wh"}""",
        };
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, "", throwTransport: true);
        var delivery = new MessageDelivery(repo, new SingleHandlerHttpClientFactory(handler), Profiles());

        var result = await delivery.SendAsync("alert");   // must not throw

        Assert.False(result.IsSuccess);
        Assert.Equal(DeliveryPath.Webhook, result.Path);
        Assert.Equal(MessagingPlatform.Discord, result.Platform);
    }

    [Fact]
    public async Task TestConnection_NamesPlatformAndPath_OnSuccess()
    {
        var repo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = PlatformJson(MessagingPlatform.Slack),
            DecryptedSecretsJson = """{"webhookUrl":"https://hooks.example/abc"}""",
        };
        var delivery = new MessageDelivery(repo,
            new SingleHandlerHttpClientFactory(new CapturingHttpHandler(HttpStatusCode.OK, "ok")), Profiles());

        var result = await delivery.TestConnectionAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectorType.Messaging, result.Type);
        Assert.Contains("Slack", result.Message);
        Assert.Contains("webhook", result.Message);
    }
}
