// Unit tests for LlmUsageReporter — records run-correlated events with tokens/cache/outcome, and is
// non-blocking (a throwing observer must never bubble out — FR-010).
using DBAIAzure.Core.Diagnostics;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.AdoTelemetry;

public sealed class LlmUsageReporterTests
{
    [Fact]
    public void Report_Success_RecordsEventWithRunIdTokensAndCache()
    {
        var observer = new CapturingObserver();
        var reporter = new LlmUsageReporter([observer], NullLogger<LlmUsageReporter>.Instance);
        LlmRunContext.CurrentRunId.Value = "run-x";

        reporter.Report(new LlmUsage("claude-sonnet-4-6", 100, 40, 60, 5, IsError: false, DurationMs: 1200));

        var recorded = Assert.Single(observer.Events);
        Assert.Equal("run-x", recorded.RunId);
        Assert.Equal(WorkflowEventType.LlmCallCompleted, recorded.EventType);
        Assert.Equal("success", recorded.Outcome);
        Assert.Equal(100, recorded.LlmInputTokens);
        Assert.Equal(40, recorded.LlmOutputTokens);
        Assert.Equal(60, recorded.LlmCacheReadTokens);
        Assert.Equal(5, recorded.LlmCacheCreationTokens);
    }

    [Fact]
    public void Report_Error_RecordsErrorOutcome_WithNullTokens()
    {
        var observer = new CapturingObserver();
        var reporter = new LlmUsageReporter([observer], NullLogger<LlmUsageReporter>.Instance);
        LlmRunContext.CurrentRunId.Value = "run-e";

        reporter.Report(new LlmUsage("claude-sonnet-4-6", 0, 0, 0, 0, IsError: true, DurationMs: 50));

        var recorded = Assert.Single(observer.Events);
        Assert.Equal("error", recorded.Outcome);
        Assert.Null(recorded.LlmInputTokens);
        Assert.Null(recorded.LlmCacheReadTokens);
    }

    [Fact]
    public void Report_ThrowingObserver_DoesNotBubbleOut()
    {
        // FR-010: telemetry is best-effort and must never disrupt the LLM call.
        var reporter = new LlmUsageReporter([new ThrowingObserver()], NullLogger<LlmUsageReporter>.Instance);
        LlmRunContext.CurrentRunId.Value = "run-t";

        var exception = Record.Exception(() =>
            reporter.Report(new LlmUsage("model", 1, 1, 0, 0, IsError: false, DurationMs: 1)));

        Assert.Null(exception);
    }

    private sealed class CapturingObserver : IWorkflowObserver
    {
        public List<WorkflowExecutionEvent> Events { get; } = [];

        public Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken cancellationToken = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : IWorkflowObserver
    {
        public Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("observer boom");
    }
}
