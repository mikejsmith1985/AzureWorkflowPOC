// Unit tests for SlackMcpReplyReader (spec-021 D4): proves it parses a Slack conversations.replies response into
// human replies (skipping bot + ignored authors, honouring the exclusive cursor, oldest-first), and that it
// resolves the Messaging connector + token and calls the configured read tool. Pure/faked — no live MCP server.
using System.Text.Json;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Integrations.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class SlackMcpReplyReaderTests
{
    private const string SlackThread = """
        {
          "ok": true,
          "messages": [
            { "type": "message", "bot_id": "B01", "text": "Your ticket has DoR gaps…", "ts": "1700000000.000100" },
            { "type": "message", "user": "U_HUMAN",   "text": "Here are the acceptance criteria.", "ts": "1700000100.000200" },
            { "type": "message", "user": "U_IGNORED", "text": "beep boop",                         "ts": "1700000200.000300" },
            { "type": "message", "user": "U_HUMAN2",  "text": "And the estimate is 5.",             "ts": "1700000300.000400" }
          ]
        }
        """;

    [Fact]
    public void ParseReplies_SkipsBotAndIgnored_ReturnsHumanRepliesOldestFirst()
    {
        var replies = SlackMcpReplyReader.ParseReplies(SlackThread, afterCursor: null, ignoreUserIds: new[] { "U_IGNORED" });

        Assert.Equal(2, replies.Count);
        Assert.Equal("U_HUMAN", replies[0].AuthorId);
        Assert.Equal("Here are the acceptance criteria.", replies[0].Text);
        Assert.Equal("1700000100.000200", replies[0].ReplyRef);
        Assert.Equal("U_HUMAN2", replies[1].AuthorId);
    }

    [Fact]
    public void ParseReplies_RespectsAfterCursor_Exclusive()
    {
        // Cursor is the first human reply's ts — only strictly-newer messages come back.
        var replies = SlackMcpReplyReader.ParseReplies(
            SlackThread, afterCursor: "1700000100.000200", ignoreUserIds: Array.Empty<string>());

        Assert.Equal(new[] { "U_IGNORED", "U_HUMAN2" }, replies.Select(reply => reply.AuthorId).ToArray());
    }

    [Fact]
    public void ParseReplies_ConvertsSlackTs_ToInstant()
    {
        var replies = SlackMcpReplyReader.ParseReplies(SlackThread, null, new[] { "U_IGNORED" });

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000100), replies[0].At);
    }

    [Fact]
    public void ParseReplies_MalformedJson_ReturnsEmpty()
    {
        Assert.Empty(SlackMcpReplyReader.ParseReplies("{ not json", null, Array.Empty<string>()));
    }

    [Fact]
    public async Task ReadNewRepliesAsync_NoReplyToolConfigured_ReturnsEmpty()
    {
        var reader = new SlackMcpReplyReader(
            new FakeRepo(MessagingConfigJson(replyTool: null), "{}"),
            new FakeGateway(SlackThread), NullLogger<SlackMcpReplyReader>.Instance);

        var replies = await reader.ReadNewRepliesAsync("C1", "1700000000.000100", null, Array.Empty<string>());

        Assert.Empty(replies);
    }

    [Fact]
    public async Task ReadNewRepliesAsync_ReadsAndParses_WhenConfigured()
    {
        var gateway = new FakeGateway(SlackThread);
        var reader = new SlackMcpReplyReader(
            new FakeRepo(MessagingConfigJson(replyTool: "conversations.replies"), """{"mcpAuthToken":"xoxp-x"}"""),
            gateway, NullLogger<SlackMcpReplyReader>.Instance);

        var replies = await reader.ReadNewRepliesAsync("C1", "1700000000.000100", null, new[] { "U_IGNORED" });

        Assert.Equal(2, replies.Count);
        Assert.Equal("conversations.replies", gateway.LastRequest!.ToolName);
        Assert.Contains("C1", gateway.LastRequest!.ArgumentsJson);   // channel substituted into the template
        Assert.Equal("xoxp-x", gateway.LastRequest!.AuthToken);      // token resolved from the encrypted secrets
    }

    private static string MessagingConfigJson(string? replyTool) =>
        JsonSerializer.Serialize(new
        {
            platform = "Slack",
            mcpServerUrl = "https://mcp.slack.com/mcp",
            mcpToolName = "slack_send_message",
            target = "C1",
            mcpReplyToolName = replyTool,
        });

    private sealed class FakeRepo : IConnectorConfigRepository
    {
        private readonly string _config;
        private readonly string _secrets;
        public FakeRepo(string config, string secrets) { _config = config; _secrets = secrets; }

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(new ConnectorConfig(type, _config, true, true, DateTimeOffset.UtcNow, null));
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<string?>(_secrets);
        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());
        public Task SaveAsync(ConnectorType type, string? n, string? s, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult r, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeGateway : IMcpMessageGateway
    {
        private readonly string _content;
        public McpReadRequest? LastRequest { get; private set; }
        public FakeGateway(string content) => _content = content;

        public Task<McpSendResult> SendAsync(McpSendRequest request, CancellationToken ct = default) =>
            Task.FromResult(new McpSendResult(true, "sent"));
        public Task<McpReadResult> ReadAsync(McpReadRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new McpReadResult(true, _content, "ok"));
        }
    }
}
