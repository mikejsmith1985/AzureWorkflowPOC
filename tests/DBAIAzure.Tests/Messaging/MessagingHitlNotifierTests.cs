// Unit tests for MessagingHitlNotifier — message composition and non-blocking delivery (T021, US2).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Integrations.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Verifies the HITL notifier composes a readable message (title, numbered questions, portal link) and
/// delivers it through <see cref="IMessageDelivery"/>, and that a delivery failure never propagates out
/// of <c>NotifyAsync</c> so a paused run is never blocked (FR-010).
/// </summary>
public sealed class MessagingHitlNotifierTests
{
    [Fact]
    public async Task NotifyAsync_ComposesTitleQuestionsAndPortalLink()
    {
        var delivery = new RecordingMessageDelivery();
        var notifier = new MessagingHitlNotifier(delivery, NullLogger<MessagingHitlNotifier>.Instance);

        await notifier.NotifyAsync(
            runId: "run-1", ticketId: "INC-42", title: "Approval needed",
            questions: ["Is the budget approved?", "Who is the owner?"],
            portalUrl: "https://portal.example/runs/run-1");

        Assert.NotNull(delivery.LastMessage);
        var message = delivery.LastMessage!;
        Assert.Contains("Approval needed", message);
        Assert.Contains("1. Is the budget approved?", message);
        Assert.Contains("2. Who is the owner?", message);
        Assert.Contains("https://portal.example/runs/run-1", message);
        Assert.Contains("INC-42", message);
    }

    [Fact]
    public async Task NotifyAsync_DeliveryThrows_DoesNotPropagate()
    {
        var delivery = new ThrowingMessageDelivery();
        var notifier = new MessagingHitlNotifier(delivery, NullLogger<MessagingHitlNotifier>.Instance);

        // Must complete without throwing even though delivery throws — the run must still pause.
        await notifier.NotifyAsync("run-2", "INC-7", "Title", ["q?"], "https://portal.example/runs/run-2");
    }

    // ── Test doubles ───────────────────────────────────────────────────

    private sealed class RecordingMessageDelivery : IMessageDelivery
    {
        public string? LastMessage { get; private set; }

        public Task<MessageDeliveryResult> SendAsync(string message, CancellationToken ct = default)
        {
            LastMessage = message;
            return Task.FromResult(new MessageDeliveryResult(true, MessagingPlatform.Slack, DeliveryPath.Webhook, "ok"));
        }

        public Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class ThrowingMessageDelivery : IMessageDelivery
    {
        public Task<MessageDeliveryResult> SendAsync(string message, CancellationToken ct = default) =>
            throw new HttpRequestException("boom");

        public Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
