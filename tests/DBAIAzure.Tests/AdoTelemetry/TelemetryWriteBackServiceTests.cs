// Unit tests for TelemetryWriteBackService — Bootstrap custom-field patch, Adaptive Tags/StoryPoints
// fallback, uncaptured-field omission, and the skip paths (no manifest / unconfigured work item type).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Web.Integrations.AzureDevOps;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.AdoTelemetry;

public sealed class TelemetryWriteBackServiceTests
{
    private const string UserStoryType = "UserStory";

    // A run with real LLM activity — model, tokens, calls, and duration all present.
    private static RunTelemetryAggregate ActiveRun(string runId) => new()
    {
        RunId = runId,
        ModelName = "claude-sonnet-4-6",
        InputTokens = 1_000,
        OutputTokens = 500,
        LlmCallCount = 3,
        DurationSeconds = 12,
    };

    private static TelemetryWriteBackService BuildService(
        RunTelemetryAggregate aggregate, ResolvedTelemetryTargets? targets, FakeBoardsClient boards) =>
        new(new FakeTelemetrySource(aggregate),
            boards,
            new FakeManifestReader(targets),
            NullLogger<TelemetryWriteBackService>.Instance);

    [Fact]
    public async Task WriteBack_Bootstrap_WritesCapturedCustomFields_OmitsUncaptured()
    {
        var boards = new FakeBoardsClient();
        var service = BuildService(ActiveRun("run-a"), BootstrapTargetsForAllFields(), boards);

        var result = await service.WriteBackAsync(
            new TelemetryWriteBackRequest("run-a", UserStoryType, WorkItemId: 42, SpeckitPhase: "Plan"));

        Assert.True(result.Attempted);
        var fields = Assert.Single(boards.FieldUpdates).Fields;

        // Captured metrics are written to their own custom fields.
        Assert.Equal("run-a", fields["Custom.AISessionID"]);
        Assert.Equal("claude-sonnet-4-6", fields["Custom.AIModelUsed"]);
        Assert.Equal(1_000, fields["Custom.AIInputTokens"]);
        Assert.Equal(500, fields["Custom.AIOutputTokens"]);
        Assert.Equal(3, fields["Custom.AIToolCalls"]);
        Assert.Equal(12, fields["Custom.AISessionDurationSec"]);
        Assert.Equal("Plan", fields["Custom.SpeckitPhase"]);
        Assert.True(fields.ContainsKey("Custom.AIEstimatedCostUSD"));

        // Metrics with no capture source today must NOT be invented.
        Assert.False(fields.ContainsKey("Custom.AICacheTokens"));
        Assert.False(fields.ContainsKey("Custom.AIAPIErrors"));
        Assert.False(fields.ContainsKey("Custom.AIToolAcceptRatePct"));
        Assert.False(fields.ContainsKey("Custom.AICacheHitRatePct"));
    }

    [Fact]
    public async Task WriteBack_Adaptive_FoldsTagsFields_AndUsesNativeFallback()
    {
        var boards = new FakeBoardsClient();
        var targets = new ResolvedTelemetryTargets(PreflightMode.Adaptive, new Dictionary<string, string>
        {
            ["Custom.AISessionID"] = "System.Tags",
            ["Custom.AIModelUsed"] = "System.Tags",
            ["Custom.SpeckitPhase"] = "System.Tags",
            ["Custom.AIInputTokens"] = "Microsoft.VSTS.Scheduling.StoryPoints",
            // AIOutputTokens et al. have no native fallback → absent → skipped.
        });
        var service = BuildService(ActiveRun("run-b"), targets, boards);

        var result = await service.WriteBackAsync(
            new TelemetryWriteBackRequest("run-b", UserStoryType, WorkItemId: 7, SpeckitPhase: "Spec"));

        Assert.True(result.Attempted);
        var fields = Assert.Single(boards.FieldUpdates).Fields;

        var tags = Assert.IsType<string>(fields["System.Tags"]);
        Assert.Contains("AISessionID=run-b", tags);
        Assert.Contains("AIModelUsed=claude-sonnet-4-6", tags);
        Assert.Contains("SpeckitPhase=Spec", tags);
        Assert.Contains("|", tags);   // pipe-separated kv encoding

        Assert.Equal(1_000, fields["Microsoft.VSTS.Scheduling.StoryPoints"]);
        Assert.False(fields.ContainsKey("Custom.AIOutputTokens"));
    }

    [Fact]
    public async Task WriteBack_EmptyTelemetry_WritesSessionIdOnly()
    {
        var boards = new FakeBoardsClient();
        var service = BuildService(RunTelemetryAggregate.Empty("run-c"), BootstrapTargetsForAllFields(), boards);

        var result = await service.WriteBackAsync(
            new TelemetryWriteBackRequest("run-c", UserStoryType, WorkItemId: 9, SpeckitPhase: null));

        Assert.True(result.Attempted);
        var fields = Assert.Single(boards.FieldUpdates).Fields;
        Assert.Equal("run-c", fields["Custom.AISessionID"]);
        Assert.False(fields.ContainsKey("Custom.AIInputTokens"));   // no LLM activity → no token fields
    }

    [Fact]
    public async Task WriteBack_NoManifest_SkipsWithoutPatching()
    {
        var boards = new FakeBoardsClient();
        var service = BuildService(ActiveRun("run-d"), targets: null, boards);

        var result = await service.WriteBackAsync(
            new TelemetryWriteBackRequest("run-d", UserStoryType, WorkItemId: 1, SpeckitPhase: null));

        Assert.False(result.Attempted);
        Assert.Empty(boards.FieldUpdates);
    }

    [Fact]
    public async Task WriteBack_UnconfiguredWorkItemType_SkipsWithoutPatching()
    {
        var boards = new FakeBoardsClient();
        var service = BuildService(ActiveRun("run-e"), BootstrapTargetsForAllFields(), boards);

        // "Epic" is not present in the telemetry field config (only UserStory + Task are).
        var result = await service.WriteBackAsync(
            new TelemetryWriteBackRequest("run-e", "Epic", WorkItemId: 5, SpeckitPhase: null));

        Assert.False(result.Attempted);
        Assert.Empty(boards.FieldUpdates);
    }

    [Fact]
    public async Task WriteBack_Bootstrap_WithCache_WritesCacheTokensAndHitRate()
    {
        var boards = new FakeBoardsClient();
        var aggregate = new RunTelemetryAggregate
        {
            RunId = "run-cache",
            ModelName = "claude-sonnet-4-6",
            InputTokens = 1000,
            OutputTokens = 200,
            CacheReadTokens = 500,
            LlmCallCount = 2,
            DurationSeconds = 5,
        };
        var service = BuildService(aggregate, BootstrapTargetsForAllFields(), boards);

        var result = await service.WriteBackAsync(
            new TelemetryWriteBackRequest("run-cache", UserStoryType, WorkItemId: 11, SpeckitPhase: "Plan"));

        Assert.True(result.Attempted);
        var fields = Assert.Single(boards.FieldUpdates).Fields;
        Assert.Equal(500, fields["Custom.AICacheTokens"]);
        Assert.Equal(33.3, fields["Custom.AICacheHitRatePct"]);   // 500 / (500 + 1000) × 100
    }

    [Fact]
    public async Task WriteBack_WithErrors_WritesApiErrorCount_EvenWithoutSuccessfulCalls()
    {
        var boards = new FakeBoardsClient();
        var aggregate = new RunTelemetryAggregate { RunId = "run-err", ErrorCount = 3 };
        var service = BuildService(aggregate, BootstrapTargetsForAllFields(), boards);

        var result = await service.WriteBackAsync(
            new TelemetryWriteBackRequest("run-err", UserStoryType, WorkItemId: 12, SpeckitPhase: null));

        Assert.True(result.Attempted);
        var fields = Assert.Single(boards.FieldUpdates).Fields;
        Assert.Equal(3, fields["Custom.AIAPIErrors"]);
        Assert.False(fields.ContainsKey("Custom.AIInputTokens"));   // no successful calls → no token fields
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    // Bootstrap mode: every configured UserStory custom field maps to itself (all were created/exist).
    private static ResolvedTelemetryTargets BootstrapTargetsForAllFields()
    {
        var refs = new[]
        {
            "Custom.AISessionID", "Custom.AIModelUsed", "Custom.AIInputTokens", "Custom.AIOutputTokens",
            "Custom.AICacheTokens", "Custom.AIEstimatedCostUSD", "Custom.AISessionDurationSec",
            "Custom.AIToolCalls", "Custom.AIToolAcceptRatePct", "Custom.AIAPIErrors",
            "Custom.AICacheHitRatePct", "Custom.SpeckitPhase",
        };
        return new ResolvedTelemetryTargets(
            PreflightMode.Bootstrap, refs.ToDictionary(r => r, r => r));
    }

    private sealed class FakeTelemetrySource : IRunTelemetrySource
    {
        private readonly RunTelemetryAggregate _aggregate;
        public FakeTelemetrySource(RunTelemetryAggregate aggregate) => _aggregate = aggregate;

        public Task<RunTelemetryAggregate> GetAggregateAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult(_aggregate);
    }

    private sealed class FakeManifestReader : IAdoTelemetryManifestReader
    {
        private readonly ResolvedTelemetryTargets? _targets;
        public FakeManifestReader(ResolvedTelemetryTargets? targets) => _targets = targets;

        public Task<ResolvedTelemetryTargets?> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_targets);
    }
}
