// Unit tests for MessagingConnectorConfig (de)serialization — platform parses from its string name (T007).
using System.Text.Json;
using System.Text.Json.Serialization;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Confirms the non-secret <see cref="MessagingConnectorConfig"/> round-trips through JSON the way the
/// settings UI writes it and the delivery layer reads it: the platform is stored as its string name and
/// optional MCP fields default to null. Uses the same JSON options as the delivery layer (web defaults +
/// string-enum converter).
/// </summary>
public sealed class MessagingConfigSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData(MessagingPlatform.Teams)]
    [InlineData(MessagingPlatform.Slack)]
    [InlineData(MessagingPlatform.Discord)]
    public void Platform_RoundTripsAsItsStringName(MessagingPlatform platform)
    {
        // The UI serializes only the platform for the webhook MVP: { "platform": "Slack" }.
        var json = JsonSerializer.Serialize(new { platform = platform.ToString() }, JsonOptions);

        var config = JsonSerializer.Deserialize<MessagingConnectorConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(platform, config!.Platform);
        Assert.Null(config.McpServerUrl);
        Assert.Null(config.Target);
    }

    [Fact]
    public void FullConfig_RoundTripsAllFields()
    {
        var original = new MessagingConnectorConfig(
            MessagingPlatform.Slack,
            McpServerUrl: "https://mcp.example/sse",
            McpToolName: "slack_post_message",
            McpArgumentTemplate: """{"channel":"{{target}}","text":"{{message}}"}""",
            Target: "C123");

        var json   = JsonSerializer.Serialize(original, JsonOptions);
        var parsed = JsonSerializer.Deserialize<MessagingConnectorConfig>(json, JsonOptions);

        Assert.Equal(original, parsed);
    }
}
