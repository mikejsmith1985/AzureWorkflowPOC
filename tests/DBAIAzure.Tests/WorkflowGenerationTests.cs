// Unit tests for WorkflowDesignSkillService.GenerateWorkflowAsync (T068-T069).
// Uses a hand-rolled fake IChatClient so no LLM call is made.

using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests;

// ── T068: GenerateWorkflowAsync returns correct graph ─────────────────────────

public sealed class WorkflowGenerationTests
{
    [Fact]
    public async Task GenerateWorkflowAsync_ValidDescription_ReturnsNodeGraph()
    {
        const string json = """
            {
              "nodes": [
                {"id": "n1", "nodeType": "Trigger",       "label": "Start",    "goalPrompt": "Entry point"},
                {"id": "n2", "nodeType": "AgenticReason", "label": "Analyse",  "goalPrompt": "Analyse input"},
                {"id": "n3", "nodeType": "FunctionNotify","label": "Notify",   "goalPrompt": "Send result"}
              ],
              "edges": [
                {"sourceNodeId": "n1", "targetNodeId": "n2"},
                {"sourceNodeId": "n2", "targetNodeId": "n3"}
              ]
            }
            """;

        var svc = BuildService(json);

        var result = await svc.GenerateWorkflowAsync("Analyse an incident and notify the team");

        Assert.Null(result.ClarifyingQuestion);
        Assert.Equal(3, result.Nodes.Count);
        Assert.Equal(2, result.Edges.Count);

        Assert.Equal("Trigger",        result.Nodes[0].NodeType);
        Assert.Equal("AgenticReason",  result.Nodes[1].NodeType);
        Assert.Equal("FunctionNotify", result.Nodes[2].NodeType);

        Assert.Equal("n1", result.Edges[0].SourceNodeId);
        Assert.Equal("n2", result.Edges[0].TargetNodeId);
        Assert.Equal("n2", result.Edges[1].SourceNodeId);
        Assert.Equal("n3", result.Edges[1].TargetNodeId);
    }

    [Fact]
    public async Task GenerateWorkflowAsync_ValidDescription_GoalPromptsPopulated()
    {
        const string json = """
            {
              "nodes": [{"id": "n1", "nodeType": "Trigger", "label": "Start", "goalPrompt": "Begin run"}],
              "edges": []
            }
            """;

        var result = await BuildService(json)
            .GenerateWorkflowAsync("Simple trigger workflow");

        Assert.Single(result.Nodes);
        Assert.Equal("Begin run", result.Nodes[0].GoalPrompt);
    }

    // ── T069: ClarifyingQuestion returned when LLM signals ambiguity ──────────

    [Fact]
    public async Task GenerateWorkflowAsync_AmbiguousDescription_ReturnsClarifyingQuestion()
    {
        const string json = """{"clarifyingQuestion": "What should happen after the trigger?"}""";

        var result = await BuildService(json)
            .GenerateWorkflowAsync("Do something with data");

        Assert.NotNull(result.ClarifyingQuestion);
        Assert.NotEmpty(result.ClarifyingQuestion);
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task GenerateWorkflowAsync_WhenChatServiceThrows_ReturnsClarifyingQuestion()
    {
        var svc = BuildService(null, shouldThrow: true);

        var result = await svc.GenerateWorkflowAsync("Doesn't matter — service will throw");

        Assert.NotNull(result.ClarifyingQuestion);
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task GenerateWorkflowAsync_InvalidJson_ReturnsClarifyingQuestion()
    {
        var result = await BuildService("not-valid-json")
            .GenerateWorkflowAsync("Trigger, then analyse, then notify");

        Assert.NotNull(result.ClarifyingQuestion);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public async Task GenerateWorkflowAsync_MarkdownFencedJson_IsParsedCorrectly()
    {
        const string fenced = """
            ```json
            {"nodes": [{"id": "n1", "nodeType": "Trigger", "label": "Start", "goalPrompt": null}], "edges": []}
            ```
            """;

        var result = await BuildService(fenced).GenerateWorkflowAsync("A simple workflow");

        Assert.Null(result.ClarifyingQuestion);
        Assert.Single(result.Nodes);
        Assert.Equal("Trigger", result.Nodes[0].NodeType);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WorkflowDesignSkillService BuildService(string? responseContent, bool shouldThrow = false)
    {
        IChatClient fakeChatClient = shouldThrow
            ? new ThrowingChatClient()
            : new StaticChatClient(responseContent ?? string.Empty);

        return new WorkflowDesignSkillService(
            new WorkflowTopologySerializer(),
            fakeChatClient,
            NullLogger<WorkflowDesignSkillService>.Instance);
    }

    /// <summary>A fake <see cref="IChatClient"/> that returns a fixed assistant response.</summary>
    private sealed class StaticChatClient(string content) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, content)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(Microsoft.Extensions.AI.ChatRole.Assistant, content);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>A fake <see cref="IChatClient"/> that fails as if the LLM were unreachable.</summary>
    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(new HttpRequestException("Simulated LLM timeout"));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new HttpRequestException("Simulated LLM timeout");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
