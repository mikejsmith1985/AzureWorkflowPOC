// Unit tests for WorkflowDesignSkillService.GenerateWorkflowAsync (T068-T069).
// Uses a hand-rolled fake IChatCompletionService so no LLM call is made.

using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
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
        var fakeChatService = shouldThrow
            ? (IChatCompletionService) new ThrowingChatService()
            : new StaticChatService(responseContent ?? string.Empty);

        return new WorkflowDesignSkillService(
            new WorkflowTopologySerializer(),
            fakeChatService,
            NullLogger<WorkflowDesignSkillService>.Instance);
    }

    private sealed class StaticChatService(string content) : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatMessageContent> result = new[]
            {
                new ChatMessageContent(AuthorRole.Assistant, content)
            };
            return Task.FromResult(result);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }

    private sealed class ThrowingChatService : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes { get; } =
            new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<ChatMessageContent>>(
                new HttpRequestException("Simulated LLM timeout"));

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }
    }
}
