// Unit tests for CostCapturingChatClient (spec-019 T006/T010): the DelegatingChatClient seam that
// re-homes the two SK cost filters onto the model call. It reads ChatResponse.Usage (streaming: the
// final UsageContent), maps it to the existing LlmUsage, and reports it to the existing usage reporter —
// so the existing ledger/binding/ingest downstream is fed unchanged. Failing first (Red).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Tests.Parity;
using DBAIAzure.Web.Services.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Verifies that model-call usage is captured at the <see cref="CostCapturingChatClient"/> seam and
/// reported to the existing <see cref="ILlmUsageReporter"/> (which drives the unchanged cost ledger):
/// token/model mapping on the non-streaming path, usage read from the final streaming update, and a
/// best-effort error report that still lets the underlying failure propagate.
/// </summary>
public sealed class CostCapturingChatClientTests
{
    // Captures every LlmUsage the client reports so tests can assert token/model/error mapping.
    private sealed class CapturingUsageReporter : ILlmUsageReporter
    {
        public List<LlmUsage> Reported { get; } = new();
        public void Report(LlmUsage usage) => Reported.Add(usage);
    }

    // A client that always throws — exercises the error-capture path without a network call.
    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("provider unreachable");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("provider unreachable");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static ChatMessage[] Prompt(string text) => new[] { new ChatMessage(ChatRole.User, text) };

    private static CostCapturingChatClient Wrap(IChatClient inner, CapturingUsageReporter reporter) =>
        new(inner, reporter, NullLogger<CostCapturingChatClient>.Instance);

    [Fact]
    public async Task NonStreaming_ReportsUsage_MappedFromResponse()
    {
        var reporter = new CapturingUsageReporter();
        var inner = new RecordedChatClient(RecordedTurn.With(
            "done", inputTokens: 120, outputTokens: 42, cacheReadTokens: 30, cacheCreationTokens: 15,
            modelId: "claude-opus-4-8"));
        using var client = Wrap(inner, reporter);

        var response = await client.GetResponseAsync(Prompt("hi"));

        Assert.Equal("done", response.Text); // pass-through unchanged
        var usage = Assert.Single(reporter.Reported);
        Assert.False(usage.IsError);
        Assert.Equal("claude-opus-4-8", usage.ModelName);
        Assert.Equal(120, usage.InputTokens);
        Assert.Equal(42, usage.OutputTokens);
        Assert.Equal(30, usage.CacheReadTokens);
        Assert.Equal(15, usage.CacheCreationTokens);
    }

    [Fact]
    public async Task Streaming_ReportsUsage_FromFinalUpdate_AndStreamsText()
    {
        var reporter = new CapturingUsageReporter();
        var inner = new RecordedChatClient(RecordedTurn.With(
            "streamed", inputTokens: 10, outputTokens: 5, modelId: "claude-haiku-4-5"));
        using var client = Wrap(inner, reporter);

        var streamedText = string.Empty;
        await foreach (var update in client.GetStreamingResponseAsync(Prompt("go")))
        {
            streamedText += update.Text;
        }

        Assert.Equal("streamed", streamedText);
        var usage = Assert.Single(reporter.Reported); // reported once, after the final update
        Assert.Equal("claude-haiku-4-5", usage.ModelName);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(5, usage.OutputTokens);
    }

    [Fact]
    public async Task OnError_ReportsErrorUsage_AndRethrows()
    {
        var reporter = new CapturingUsageReporter();
        using var client = Wrap(new ThrowingChatClient(), reporter);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync(Prompt("hi")));

        var usage = Assert.Single(reporter.Reported);
        Assert.True(usage.IsError);
        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.OutputTokens);
    }
}
