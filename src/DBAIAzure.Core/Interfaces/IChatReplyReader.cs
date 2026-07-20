// Reads human replies from the conversation thread a DoR run owns (spec-021 D4). Kept behind an interface so
// the reply pump is testable and the Slack-MCP implementation can be swapped without touching the pump.
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Reads new human replies in a conversation thread. Best-effort: returns an empty list on a transient failure
/// (the pump simply tries again next cycle). Implemented over the existing Slack MCP integration (poll the
/// thread), not a separate Events API app.
/// </summary>
public interface IChatReplyReader
{
    /// <summary>
    /// Returns human replies in <paramref name="threadRef"/> (excluding the bot's own messages and
    /// <paramref name="ignoreUserIds"/>). <paramref name="afterCursor"/> is the last processed reply id when the
    /// implementation supports server-side cursoring; the pump also de-duplicates in-process.
    /// </summary>
    Task<IReadOnlyList<ChatReply>> ReadNewRepliesAsync(
        string channelId,
        string threadRef,
        string? afterCursor,
        IReadOnlyCollection<string> ignoreUserIds,
        CancellationToken ct = default);
}

/// <summary>A single human reply captured from a conversation thread.</summary>
public sealed record ChatReply(string ReplyRef, string AuthorId, string Text, DateTimeOffset At);
