// Reads DoR conversation replies over the existing Slack MCP integration (spec-021 D4). Outbound sending is
// already MCP; this reads thread replies by calling the Messaging connector's configured thread-read tool
// (Slack conversations.replies / slack_read_thread) and parsing the returned messages into human replies.
using System.Text.Json;
using System.Text.Json.Serialization;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Integrations.Messaging;

/// <summary>
/// Slack-MCP implementation of <see cref="IChatReplyReader"/>. Polls the thread a DoR run owns over the same MCP
/// connection used for sending: it resolves the Messaging connector (server URL + auth token + the reply-read
/// tool name/template, hot-reloaded per call), invokes the read tool, and parses the Slack-shaped response.
/// Best-effort — a missing reply-tool config, a transient failure, or an unparseable response yields an empty
/// list rather than throwing, so the reply pump stays non-blocking.
/// </summary>
public sealed class SlackMcpReplyReader : IChatReplyReader
{
    // Default arguments for Slack's conversations.replies tool; overridable per connector for other MCP servers.
    private const string DefaultReplyArgumentTemplate = """{"channel_id":"{{channel}}","thread_ts":"{{thread}}"}""";
    private const string ChannelPlaceholder = "{{channel}}";
    private const string ThreadPlaceholder = "{{thread}}";

    private static readonly JsonSerializerOptions ConfigJsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly IConnectorConfigRepository _configRepo;
    private readonly IMcpMessageGateway _gateway;
    private readonly ILogger<SlackMcpReplyReader> _logger;

    public SlackMcpReplyReader(
        IConnectorConfigRepository configRepo, IMcpMessageGateway gateway, ILogger<SlackMcpReplyReader> logger)
    {
        _configRepo = configRepo;
        _gateway = gateway;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatReply>> ReadNewRepliesAsync(
        string channelId, string threadRef, string? afterCursor,
        IReadOnlyCollection<string> ignoreUserIds, CancellationToken ct = default)
    {
        var config = await ResolveMessagingConfigAsync(ct);
        if (config is null
            || string.IsNullOrWhiteSpace(config.McpServerUrl)
            || string.IsNullOrWhiteSpace(config.McpReplyToolName))
        {
            // Reply reading is not configured (no MCP server or no read tool) — nothing to poll.
            return Array.Empty<ChatReply>();
        }

        var authToken = await ResolveAuthTokenAsync(ct);
        var argumentsJson = BuildReadArguments(config.McpReplyArgumentTemplate, channelId, threadRef);
        var request = new McpReadRequest(config.McpServerUrl!, config.McpReplyToolName!, argumentsJson, authToken);

        var result = await _gateway.ReadAsync(request, ct);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.ContentJson))
        {
            _logger.LogDebug("DoR reply read returned no content for thread {Thread}: {Message}", threadRef, result.Message);
            return Array.Empty<ChatReply>();
        }

        return ParseReplies(result.ContentJson!, afterCursor, ignoreUserIds);
    }

    /// <summary>
    /// Parses a Slack <c>conversations.replies</c>-shaped response into human replies newer than
    /// <paramref name="afterCursor"/> (exclusive), oldest-first. Skips the bot's own messages (Slack marks them
    /// with a <c>bot_id</c>) and any author in <paramref name="ignoreUserIds"/>. Pure and defensive: any parse
    /// problem yields an empty list. Exposed for unit testing without a live MCP server.
    /// </summary>
    public static IReadOnlyList<ChatReply> ParseReplies(
        string contentJson, string? afterCursor, IReadOnlyCollection<string> ignoreUserIds)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contentJson);
        }
        catch (JsonException)
        {
            return Array.Empty<ChatReply>();
        }

        using (document)
        {
            if (!TryGetMessages(document.RootElement, out var messages))
                return Array.Empty<ChatReply>();

            var afterTs = ParseSlackTs(afterCursor);
            var replies = new List<ChatReply>();
            foreach (var message in messages.EnumerateArray())
                AppendReplyIfHuman(message, afterTs, ignoreUserIds, replies);

            // Oldest-first so the pump submits replies to the workflow in the order they were written.
            replies.Sort((first, second) => ParseSlackTs(first.ReplyRef).CompareTo(ParseSlackTs(second.ReplyRef)));
            return replies;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<MessagingConnectorConfig?> ResolveMessagingConfigAsync(CancellationToken ct)
    {
        try
        {
            var connector = await _configRepo.GetAsync(ConnectorType.Messaging, ct);
            return connector?.NonSecretConfig is { } json
                ? JsonSerializer.Deserialize<MessagingConnectorConfig>(json, ConfigJsonOptions)
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve the Messaging connector for DoR reply reading.");
            return null;
        }
    }

    private async Task<string?> ResolveAuthTokenAsync(CancellationToken ct)
    {
        try
        {
            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.Messaging, ct);
            if (string.IsNullOrWhiteSpace(secretsJson))
                return null;
            using var doc = JsonDocument.Parse(secretsJson);
            return doc.RootElement.TryGetProperty("mcpAuthToken", out var token) ? token.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve the Messaging MCP auth token for DoR reply reading.");
            return null;
        }
    }

    /// <summary>Substitutes the channel and thread ids into the read-tool argument template (JSON-escaped).</summary>
    private static string BuildReadArguments(string? templateJson, string channelId, string threadRef)
    {
        var template = string.IsNullOrWhiteSpace(templateJson) ? DefaultReplyArgumentTemplate : templateJson;
        return template
            .Replace(ChannelPlaceholder, JsonEscapeInner(channelId))
            .Replace(ThreadPlaceholder, JsonEscapeInner(threadRef));
    }

    /// <summary>Adds a single message to <paramref name="replies"/> when it is a new, human, non-ignored reply.</summary>
    private static void AppendReplyIfHuman(
        JsonElement message, double afterTs, IReadOnlyCollection<string> ignoreUserIds, List<ChatReply> replies)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return;

        // Slack tags bot-authored messages with a bot_id — those are the workflow's own posts, never human input.
        if (message.TryGetProperty("bot_id", out var botId)
            && botId.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(botId.GetString()))
            return;

        var timestamp = ReadString(message, "ts");
        if (string.IsNullOrEmpty(timestamp) || ParseSlackTs(timestamp) <= afterTs)
            return; // older than or equal to the last-seen reply (exclusive cursor)

        var author = ReadString(message, "user");
        if (author.Length == 0)
            author = ReadString(message, "user_id");
        if (ignoreUserIds.Contains(author))
            return;

        replies.Add(new ChatReply(timestamp, author, ReadString(message, "text"), FromSlackTs(timestamp)));
    }

    /// <summary>Finds the messages array — at the root object's <c>messages</c> property, or the root if it is an array.</summary>
    private static bool TryGetMessages(JsonElement root, out JsonElement messages)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            messages = root;
            return true;
        }
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("messages", out messages) && messages.ValueKind == JsonValueKind.Array)
        {
            return true;
        }
        messages = default;
        return false;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Parses a Slack <c>ts</c> ("1700000000.000100") to a comparable number; 0 for null/blank/invalid.</summary>
    private static double ParseSlackTs(string? slackTimestamp) =>
        double.TryParse(slackTimestamp, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0d;

    /// <summary>Converts a Slack <c>ts</c> to an instant using its whole-second part.</summary>
    private static DateTimeOffset FromSlackTs(string slackTimestamp)
    {
        var dotIndex = slackTimestamp.IndexOf('.');
        var seconds = dotIndex >= 0 ? slackTimestamp[..dotIndex] : slackTimestamp;
        return long.TryParse(seconds, out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : DateTimeOffset.UtcNow;
    }

    // Serialize a value as a JSON string then strip the surrounding quotes, leaving escaped contents suitable
    // for placement inside an existing pair of quotes in the argument template.
    private static string JsonEscapeInner(string value)
    {
        var quoted = JsonSerializer.Serialize(value);
        return quoted.Length >= 2 ? quoted[1..^1] : quoted;
    }
}
