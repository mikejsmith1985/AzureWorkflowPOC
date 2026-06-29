// Live round-trip test proving the connector captures real usage (spec-016 FR-001/FR-002/SC-002).
// Skips automatically when ANTHROPIC_API_KEY is absent. Run with: dotnet test --filter "Category=Integration".
using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace DBAIAzure.Tests.Integration;

/// <summary>
/// Makes a genuine Anthropic Messages API call through <see cref="AnthropicChatCompletionService"/> and
/// asserts that the usage reporter received real token counts and a model id — the live proof that the
/// capture fix works end-to-end against the provider (unit tests cover the parsing/aggregation logic).
/// </summary>
[Trait("Category", "Integration")]
public sealed class AnthropicUsageCaptureIntegrationTests
{
    [Fact]
    public async Task Connector_RealCall_ReportsUsageWithTokensAndModel()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6";
        if (apiKey is null)
            return; // Skip when no key is present (standard unit run is never blocked).

        var reporter = new CapturingReporter();
        using var connector = new AnthropicChatCompletionService(apiKey, model, reporter);

        var history = new ChatHistory();
        history.AddUserMessage("Respond with the single word READY.");

        await connector.GetChatMessageContentsAsync(history);

        var usage = Assert.Single(reporter.Usages);
        Assert.False(usage.IsError);
        Assert.True(usage.InputTokens > 0, "input tokens should be captured from the real response");
        Assert.True(usage.OutputTokens > 0, "output tokens should be captured from the real response");
        Assert.False(string.IsNullOrWhiteSpace(usage.ModelName), "the model id should be captured");
    }

    private sealed class CapturingReporter : ILlmUsageReporter
    {
        public List<LlmUsage> Usages { get; } = [];
        public void Report(LlmUsage usage) => Usages.Add(usage);
    }
}
