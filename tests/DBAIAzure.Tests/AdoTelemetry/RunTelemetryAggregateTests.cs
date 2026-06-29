// Unit tests for RunTelemetryAggregate.FromSamples — token sums, call count, duration, latest model.
using DBAIAzure.Core.Models.AdoTelemetry;
using Xunit;

namespace DBAIAzure.Tests.AdoTelemetry;

public sealed class RunTelemetryAggregateTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 6, 29, 10, 5, 0, TimeSpan.Zero);

    [Fact]
    public void FromSamples_SumsTokens_CountsCalls_AndRoundsDuration()
    {
        var samples = new[]
        {
            new LlmTelemetrySample(Earlier, DurationMs: 1500, "claude-haiku-4-5", InputTokens: 100, OutputTokens: 40),
            new LlmTelemetrySample(Later, DurationMs: 800, "claude-sonnet-4-6", InputTokens: 250, OutputTokens: 60),
        };

        var aggregate = RunTelemetryAggregate.FromSamples("run-1", samples);

        Assert.Equal("run-1", aggregate.RunId);
        Assert.Equal(350, aggregate.InputTokens);
        Assert.Equal(100, aggregate.OutputTokens);
        Assert.Equal(2, aggregate.LlmCallCount);
        Assert.Equal(2, aggregate.DurationSeconds);   // round(2300ms / 1000)
        Assert.True(aggregate.HasLlmActivity);
    }

    [Fact]
    public void FromSamples_PicksMostRecentlyUsedModel()
    {
        var samples = new[]
        {
            new LlmTelemetrySample(Later, DurationMs: 0, "claude-sonnet-4-6", 1, 1),
            new LlmTelemetrySample(Earlier, DurationMs: 0, "claude-haiku-4-5", 1, 1),
        };

        var aggregate = RunTelemetryAggregate.FromSamples("run-2", samples);

        Assert.Equal("claude-sonnet-4-6", aggregate.ModelName);
    }

    [Fact]
    public void FromSamples_NullTokenFields_TreatedAsZero()
    {
        var samples = new[]
        {
            new LlmTelemetrySample(Earlier, DurationMs: null, ModelName: null, InputTokens: null, OutputTokens: null),
        };

        var aggregate = RunTelemetryAggregate.FromSamples("run-3", samples);

        Assert.Equal(0, aggregate.InputTokens);
        Assert.Equal(0, aggregate.OutputTokens);
        Assert.Equal(1, aggregate.LlmCallCount);
        Assert.Null(aggregate.ModelName);
    }

    [Fact]
    public void FromSamples_NoSamples_YieldsEmptyAggregate()
    {
        var aggregate = RunTelemetryAggregate.FromSamples("run-4", Array.Empty<LlmTelemetrySample>());

        Assert.False(aggregate.HasLlmActivity);
        Assert.Equal("run-4", aggregate.RunId);
        Assert.Equal(0, aggregate.LlmCallCount);
    }
}
