// Reads DoR conversation replies over the existing Slack MCP integration (spec-021 D4). The outbound path is
// already MCP; reading thread replies requires the configured Slack MCP server to expose a thread-read /
// conversations.replies tool. Until that tool is present/verified against a live server, this returns no
// replies (the pump then simply does nothing), keeping the seam wired without a second Slack integration.
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Integrations.Messaging;

/// <summary>
/// Slack-MCP implementation of <see cref="IChatReplyReader"/>. Polls the thread the DoR run owns over the same
/// MCP connection used for sending. Best-effort: a transient failure — or a Slack MCP server without a
/// thread-read tool — yields an empty result rather than throwing, so the reply pump stays non-blocking.
/// </summary>
/// <remarks>
/// The live thread-read call (Slack <c>conversations.replies</c>) is added to the MCP gateway once verified
/// against the configured Slack MCP server; the parsing of its response shape is server-specific. Today this
/// returns no replies, which is safe: paused conversations are still resolvable via the orchestrator's
/// <c>SubmitReply</c> seam (e.g. from a webhook or the console) and the whole engine + pump is exercised by tests.
/// </remarks>
public sealed class SlackMcpReplyReader : IChatReplyReader
{
    private readonly ILogger<SlackMcpReplyReader> _logger;

    public SlackMcpReplyReader(ILogger<SlackMcpReplyReader> logger) => _logger = logger;

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatReply>> ReadNewRepliesAsync(
        string channelId, string threadRef, string? afterCursor,
        IReadOnlyCollection<string> ignoreUserIds, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Slack MCP thread-read not yet wired to a live read tool; no replies read for thread {Thread}.", threadRef);
        return Task.FromResult<IReadOnlyList<ChatReply>>(Array.Empty<ChatReply>());
    }
}
