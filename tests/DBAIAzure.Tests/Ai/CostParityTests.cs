// Cost/metering parity for the MAF model layer (spec-019 T035 / SC-004): the CostCapturingChatClient
// captures token usage from a pinned response identically to the pre-migration build, so the downstream
// cost estimate is unchanged (0% delta), tagged by model.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Tests.Parity;
using DBAIAzure.Web.Services.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Pins a model response's token usage (as the baseline SK connector would have captured it) and asserts
/// the MAF <see cref="CostCapturingChatClient"/> captures the same input/output/cache tokens and model, so
/// <see cref="ModelPricing.EstimateCostUsd"/> yields an identical cost — the metering-parity guarantee.
/// </summary>
public sealed class CostParityTests
{
    private const string Model = "claude-opus-4-8";
    private const int InputTokens = 1000;
    private const int OutputTokens = 500;
    private const int CacheReadTokens = 200;
    private const int CacheCreationTokens = 100;

    private sealed class CapturingReporter : ILlmUsageReporter
    {
        public List<LlmUsage> Reported { get; } = new();
        public void Report(LlmUsage usage) => Reported.Add(usage);
    }

    [Fact]
    public async Task CapturedUsage_MatchesBaselineTokens_AndYieldsIdenticalCost()
    {
        var reporter = new CapturingReporter();
        var inner = new RecordedChatClient(RecordedTurn.With(
            "ok", InputTokens, OutputTokens, CacheReadTokens, CacheCreationTokens, Model));
        using var client = new CostCapturingChatClient(inner, reporter, NullLogger<CostCapturingChatClient>.Instance);

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });

        var captured = Assert.Single(reporter.Reported);

        // Token parity — every dimension the SK connector recorded is recorded identically here.
        Assert.Equal(Model, captured.ModelName);
        Assert.Equal(InputTokens, captured.InputTokens);
        Assert.Equal(OutputTokens, captured.OutputTokens);
        Assert.Equal(CacheReadTokens, captured.CacheReadTokens);
        Assert.Equal(CacheCreationTokens, captured.CacheCreationTokens);

        // Cost parity — the same tokens through the same pricing model produce the same estimate (0% delta).
        var baselineCost = ModelPricing.EstimateCostUsd(
            Model, InputTokens, OutputTokens, CacheReadTokens, CacheCreationTokens);
        var capturedCost = ModelPricing.EstimateCostUsd(
            captured.ModelName, captured.InputTokens, captured.OutputTokens,
            captured.CacheReadTokens, captured.CacheCreationTokens);

        Assert.Equal(baselineCost, capturedCost);
        Assert.True(capturedCost is > 0);
    }
}
