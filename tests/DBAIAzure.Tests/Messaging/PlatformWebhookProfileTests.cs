// Unit tests for the per-platform webhook profiles — body shape + success signal (T008, FR-006).
using System.Text.Json;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Verifies each platform's incoming-webhook contract: the request body shape and the distinct success
/// signal (Teams "1", Slack "ok", Discord HTTP 204). A generic "any 2xx" check would falsely pass, so each
/// profile's <c>IsSuccess</c> is exercised against both its accepted and a near-miss response.
/// </summary>
public sealed class PlatformWebhookProfileTests
{
    // ── Teams ──────────────────────────────────────────────────────────

    [Fact]
    public void Teams_BuildsAdaptiveCard_WithMessageText()
    {
        var profile = new TeamsWebhookProfile();

        using var doc = JsonDocument.Parse(profile.BuildBody("hello \"world\""));
        var text = doc.RootElement
            .GetProperty("attachments")[0].GetProperty("content")
            .GetProperty("body")[0].GetProperty("text").GetString();

        Assert.Equal(MessagingPlatform.Teams, profile.Platform);
        Assert.Equal("hello \"world\"", text);   // message preserved, JSON stays valid
    }

    [Theory]
    [InlineData(200, "1", true)]
    [InlineData(200, "\"1\"", true)]
    [InlineData(200, "0", false)]
    [InlineData(202, "1", true)]
    [InlineData(401, "1", false)]
    public void Teams_Success_RequiresBodyOne(int status, string body, bool expected) =>
        Assert.Equal(expected, new TeamsWebhookProfile().IsSuccess(status, body));

    // ── Slack ──────────────────────────────────────────────────────────

    [Fact]
    public void Slack_BuildsTextBody()
    {
        var profile = new SlackWebhookProfile();
        using var doc = JsonDocument.Parse(profile.BuildBody("deploy done"));
        Assert.Equal("deploy done", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal(MessagingPlatform.Slack, profile.Platform);
    }

    [Theory]
    [InlineData(200, "ok", true)]
    [InlineData(200, "invalid_payload", false)]
    [InlineData(404, "ok", false)]
    public void Slack_Success_RequiresBodyOk(int status, string body, bool expected) =>
        Assert.Equal(expected, new SlackWebhookProfile().IsSuccess(status, body));

    // ── Discord ────────────────────────────────────────────────────────

    [Fact]
    public void Discord_BuildsContentBody()
    {
        var profile = new DiscordWebhookProfile();
        using var doc = JsonDocument.Parse(profile.BuildBody("alert"));
        Assert.Equal("alert", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal(MessagingPlatform.Discord, profile.Platform);
    }

    [Theory]
    [InlineData(204, "", true)]
    [InlineData(200, "", false)]   // Discord signals success only with 204 No Content
    [InlineData(400, "", false)]
    public void Discord_Success_Requires204(int status, string body, bool expected) =>
        Assert.Equal(expected, new DiscordWebhookProfile().IsSuccess(status, body));
}
