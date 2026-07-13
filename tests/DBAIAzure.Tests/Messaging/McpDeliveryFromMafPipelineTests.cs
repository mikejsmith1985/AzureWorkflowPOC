// US4 MCP delivery parity (spec-019 T045): a HITL notification raised by the MAF intake pipeline is
// delivered through the real messaging chain to the MCP send-message tool — proving MCP-backed delivery
// works from a MAF workflow exactly as before. The gateway is already framework-neutral (MCP SDK, no SK),
// so no re-expression was needed (T046); this verifies the end-to-end path.
using System.Net;
using System.Text.Json;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Parity;
using DBAIAzure.Web.Integrations.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Drives a MAF intake run to its clarification gate and asserts the resulting human-in-the-loop
/// notification reaches the MCP send-message tool through the production delivery chain
/// (<see cref="MessagingHitlNotifier"/> → <see cref="MessageDelivery"/> → <see cref="IMcpMessageGateway"/>).
/// </summary>
public sealed class McpDeliveryFromMafPipelineTests
{
    private static TicketState SampleTicket => new() { TicketId = "INC0007", Title = "Sample", Description = "Sample description." };

    private static IPlatformWebhookProfile[] Profiles() =>
        [new TeamsWebhookProfile(), new SlackWebhookProfile(), new DiscordWebhookProfile()];

    [Fact]
    public async Task NotReadyRun_DeliversHitlNotification_ViaMcpTool()
    {
        // Messaging is configured to deliver via an MCP server; a fake gateway records the tool invocation.
        var gateway = new FakeMcpMessageGateway(succeeds: true);
        var configRepo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = JsonSerializer.Serialize(new
            {
                platform = MessagingPlatform.Slack.ToString(),
                mcpServerUrl = "https://mcp.example/sse",
                mcpToolName = "send_message",
                target = "C123",
            }),
            DecryptedSecretsJson = """{"mcpAuthToken":"tok-abc"}""",
        };
        var delivery = new MessageDelivery(
            configRepo,
            new SingleHandlerHttpClientFactory(new CapturingHttpHandler(HttpStatusCode.OK, "")),
            Profiles(),
            gateway);
        var notifier = new MessagingHitlNotifier(delivery, NullLogger<MessagingHitlNotifier>.Instance);

        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),
            RecordedTurn.With("{\"is_ready\":false,\"missing_fields\":[\"target environment\"],\"reasoning\":\"missing env\"}", 30, 10),
            RecordedTurn.With("[\"What is the target environment?\"]", 35, 8),
        }, repeatLast: true);

        var orchestrator = new PipelineOrchestrator(
            chatClient, hitlNotifier: notifier);

        orchestrator.StartRun(SampleTicket);

        // The HITL notification is fire-and-forget; wait for the MCP tool call to be recorded.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && gateway.LastRequest is null)
        {
            await Task.Delay(25);
        }

        Assert.NotNull(gateway.LastRequest);
        Assert.Equal("send_message", gateway.LastRequest!.ToolName);
        Assert.Equal("https://mcp.example/sse", gateway.LastRequest.ServerUrl);
        Assert.Contains("target environment", gateway.LastRequest.Message); // the clarifying question was delivered
    }
}
