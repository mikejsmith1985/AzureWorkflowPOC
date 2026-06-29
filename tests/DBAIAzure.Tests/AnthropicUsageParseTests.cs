// Unit tests for AnthropicChatCompletionService.BuildUsage — parsing usage (incl. both cache fields)
// and the model from a raw Messages API response body, with no HTTP round-trip.
using DBAIAzure.Connectors;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class AnthropicUsageParseTests
{
    [Fact]
    public void BuildUsage_ParsesTokens_Cache_AndModel()
    {
        const string body = """
        {
          "model": "claude-sonnet-4-6",
          "content": [{ "type": "text", "text": "hi" }],
          "usage": {
            "input_tokens": 1200,
            "output_tokens": 340,
            "cache_read_input_tokens": 800,
            "cache_creation_input_tokens": 50
          }
        }
        """;

        var usage = AnthropicChatCompletionService.BuildUsage(body, "fallback-model", durationMs: 1500);

        Assert.Equal("claude-sonnet-4-6", usage.ModelName);
        Assert.Equal(1200, usage.InputTokens);
        Assert.Equal(340, usage.OutputTokens);
        Assert.Equal(800, usage.CacheReadTokens);
        Assert.Equal(50, usage.CacheCreationTokens);
        Assert.False(usage.IsError);
        Assert.Equal(1500, usage.DurationMs);
    }

    [Fact]
    public void BuildUsage_NoUsageBlock_ZerosTokens_AndUsesFallbackModel()
    {
        const string body = """{ "content": [{ "type": "text", "text": "hi" }] }""";

        var usage = AnthropicChatCompletionService.BuildUsage(body, "fallback-model", durationMs: 10);

        Assert.Equal("fallback-model", usage.ModelName);
        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.CacheReadTokens);
        Assert.Equal(0, usage.CacheCreationTokens);
    }
}
