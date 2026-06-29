// Unit tests for MCP argument-template substitution — placeholders, JSON-escaping, default (T026, FR-007).
using System.Text.Json;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Verifies <see cref="McpArgumentTemplate.Substitute"/> replaces <c>{{target}}</c>/<c>{{message}}</c>
/// with JSON-escaped values, keeps the result valid JSON when the message contains quotes/newlines, and
/// falls back to the default template when none is supplied.
/// </summary>
public sealed class McpArgumentTemplateTests
{
    [Fact]
    public void Substitute_FillsPlaceholders_ProducingValidJson()
    {
        var template = """{"channel":"{{target}}","text":"{{message}}"}""";

        var json = McpArgumentTemplate.Substitute(template, "C123", "hello");

        using var doc = JsonDocument.Parse(json);   // must be valid JSON
        Assert.Equal("C123", doc.RootElement.GetProperty("channel").GetString());
        Assert.Equal("hello", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void Substitute_JsonEscapes_QuotesAndNewlines()
    {
        var template = """{"text":"{{message}}"}""";

        var json = McpArgumentTemplate.Substitute(template, "ignored", "line1\nsays \"hi\"");

        using var doc = JsonDocument.Parse(json);   // escaping must keep it parseable
        Assert.Equal("line1\nsays \"hi\"", doc.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Substitute_UsesDefaultTemplate_WhenBlank(string? template)
    {
        var json = McpArgumentTemplate.Substitute(template, "U9", "ping");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("U9", doc.RootElement.GetProperty("target").GetString());
        Assert.Equal("ping", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void DefaultFor_Slack_ProducesChannelIdAndMessage_AsSlackToolRequires()
    {
        // Slack's slack_send_message rejects any keys other than channel_id/message with a no_text error.
        var json = McpArgumentTemplate.Substitute(McpArgumentTemplate.DefaultFor(MessagingPlatform.Slack), "C123", "hi");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("C123", doc.RootElement.GetProperty("channel_id").GetString());
        Assert.Equal("hi", doc.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(MessagingPlatform.Teams)]
    [InlineData(MessagingPlatform.Discord)]
    public void DefaultFor_PlatformWithoutVerifiedSchema_UsesGenericTargetTextKeys(MessagingPlatform platform)
    {
        // Platforms with no verified tool schema keep the generic template; operators override it per tool.
        var json = McpArgumentTemplate.Substitute(McpArgumentTemplate.DefaultFor(platform), "X1", "ping");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("X1", doc.RootElement.GetProperty("target").GetString());
        Assert.Equal("ping", doc.RootElement.GetProperty("text").GetString());
    }
}
