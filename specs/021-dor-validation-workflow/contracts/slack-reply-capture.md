# Contract: Slack Reply Capture (over the existing MCP gateway)

Clarify Q1 / D4. Inbound replies are read by **polling the thread over the existing Slack MCP gateway** — no
separate Events API app, no public inbound endpoint. Outbound stays on `IMessageDelivery`.

## Interface

```csharp
public interface IChatReplyReader
{
    // Returns human replies in the given thread newer than `afterCursor` (exclusive), oldest-first.
    // Excludes the bot's own messages and configured ignore_user_ids. Best-effort; empty on transient failure.
    Task<IReadOnlyList<ChatReply>> ReadNewRepliesAsync(
        string channelId, string threadRef, string? afterCursor,
        IReadOnlyCollection<string> ignoreUserIds, CancellationToken ct = default);
}

public sealed record ChatReply(string ReplyRef, string AuthorId, string Text, DateTimeOffset At);
```

## Gateway extension

`IMcpMessageGateway` gains a read/history call mapping to the Slack MCP server's `conversations.replies`
(or `slack_read_thread`) tool. If the configured MCP server is send-only today, extend it with that tool; the
gateway resolves the Slack token per call (hot-reload), consistent with the send path.

## Reply-pump behavior (in `DorSlaSweeperService` or a sibling pass)

1. For each instance in `AwaitingResponse`/`Escalated`, call `ReadNewRepliesAsync(ActiveChannelId, ThreadRef,
   LastSeenReplyRef, ignore, ct)`.
2. Any non-bot reply not on the ignore list = human input (FR-011). Advance `LastSeenReplyRef` (dedup).
3. Feed the reply into the workflow via `MafWorkflowSession.RespondAsync(pendingRequest, replyState)` → re-enters
   `Reviewing` for reply-eval (AI). Poll latency is immaterial vs hour-long SLAs.
4. Reply-timeout and SLA are independent: a reply after the reply-timeout but before the SLA is still processed
   (FR-015). Only the SLA sweeper drives escalation/manual-exit on deadline.

## Idempotency & threading

The workflow owns exactly one thread per ticket (`ThreadRef`), created by its first outreach; only replies in
that thread are captured. `ReplyRef` dedup prevents re-processing across poll cycles and restarts.
