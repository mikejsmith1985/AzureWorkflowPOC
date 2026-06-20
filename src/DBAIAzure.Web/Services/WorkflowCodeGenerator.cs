// Generates and refines Semantic Kernel process code from workflow definitions,
// streaming tokens to the UI for a live typing effect.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.CompilerServices;
using System.Text;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Implements <see cref="IWorkflowCodeGenerator"/> using Semantic Kernel's streaming
/// chat completion. Each call to <see cref="GenerateAsync"/> builds a prompt from the
/// serialised topology and the full conversation history, then streams tokens back to the
/// caller via an <see cref="Action{T}"/> callback.
/// <see cref="RefineAsync"/> appends the user's instruction as a new chat turn and computes
/// a line-level diff against the previous code using a Myers-style algorithm.
/// </summary>
public sealed class WorkflowCodeGenerator : IWorkflowCodeGenerator
{
    private readonly WorkflowTopologySerializer _serializer;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<WorkflowCodeGenerator> _logger;

    /// <summary>
    /// Initialises the generator with the topology serializer and SK chat completion service.
    /// </summary>
    public WorkflowCodeGenerator(
        WorkflowTopologySerializer serializer,
        IChatCompletionService chatService,
        ILogger<WorkflowCodeGenerator> logger)
    {
        _serializer  = serializer;
        _chatService = chatService;
        _logger      = logger;
    }

    /// <inheritdoc/>
    public async Task<string> GenerateAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<WorkflowChatMessage> chatHistory,
        Action<string> onToken,
        CancellationToken cancellationToken = default)
    {
        var history = BuildChatHistory(workflow, chatHistory, userInstruction: null);
        return await StreamResponseAsync(history, onToken, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<(string UpdatedCode, DiffResult Diff)> RefineAsync(
        string previousCode,
        string instruction,
        WorkflowDefinition workflow,
        Action<string> onToken,
        CancellationToken cancellationToken = default)
    {
        var history = BuildChatHistory(workflow, workflow.ChatHistory, instruction);
        // Provide the previous code as context so the LLM knows what to change.
        history.AddAssistantMessage(previousCode);
        history.AddUserMessage(instruction);

        var updatedCode = await StreamResponseAsync(history, onToken, cancellationToken)
            .ConfigureAwait(false);

        var diff = ComputeLinesDiff(previousCode, updatedCode);
        return (updatedCode, diff);
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private ChatHistory BuildChatHistory(
        WorkflowDefinition workflow,
        IReadOnlyList<WorkflowChatMessage> domainHistory,
        string? userInstruction)
    {
        var topology = _serializer.Serialize(workflow);
        var triggerContext = BuildTriggerContext(workflow);
        var history  = new ChatHistory();
        history.AddSystemMessage(BuildSystemPrompt(topology, triggerContext));

        foreach (var message in domainHistory)
        {
            if (message.Role == ChatRole.User)
                history.AddUserMessage(message.Content);
            else
                history.AddAssistantMessage(message.Content);
        }

        if (userInstruction is not null)
            history.AddUserMessage(userInstruction);

        return history;
    }

    /// <summary>
    /// Extracts the Trigger node's GoalPrompt and initialDataDescription so the code generator
    /// can emit an entry-point comment that documents the workflow's starting contract.
    /// Returns null when no Trigger node is present (legacy or untriggered workflows).
    /// </summary>
    private static string? BuildTriggerContext(WorkflowDefinition workflow)
    {
        var triggerNode = workflow.Nodes.FirstOrDefault(n => n.NodeType == WorkflowNodeType.Trigger);
        if (triggerNode is null || string.IsNullOrWhiteSpace(triggerNode.GoalPrompt))
            return null;

        var description = triggerNode.GoalPrompt;
        if (!string.IsNullOrWhiteSpace(triggerNode.FunctionConfig))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(triggerNode.FunctionConfig);
                if (doc.RootElement.TryGetProperty("initialDataDescription", out var prop)
                    && !string.IsNullOrWhiteSpace(prop.GetString()))
                {
                    description += $"\n    /// Available data: {prop.GetString()}";
                }
            }
            catch { }
        }

        return description;
    }

    private static string BuildSystemPrompt(string topology, string? triggerContext)
    {
        // Emit an entry-point XML doc comment that documents the workflow's trigger contract
        // so code consumers immediately understand what starts the process and what data arrives.
        var entryPointComment = triggerContext is not null
            ? $"""
              At the top of the generated file, before the namespace declaration, emit this XML doc region:
              /// <summary>Entry point: {triggerContext}</summary>

              """
            : string.Empty;

        return $"""
        You are an expert Semantic Kernel developer. Generate production-ready C# code
        implementing the workflow described below as a KernelProcess with typed steps and events.
        Follow .NET 8 and Semantic Kernel 1.x conventions. Add XML doc comments to every public type.
        {entryPointComment}
        {topology}
        """;
    }

    private async Task<string> StreamResponseAsync(
        ChatHistory history,
        Action<string> onToken,
        CancellationToken cancellationToken)
    {
        var codeBuilder = new StringBuilder();
        try
        {
            await foreach (var chunk in _chatService
                .GetStreamingChatMessageContentsAsync(history, cancellationToken: cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                var token = chunk.Content ?? string.Empty;
                if (token.Length > 0)
                {
                    onToken(token);
                    codeBuilder.Append(token);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM streaming call failed.");
            throw new LlmUnavailableException(
                "The assistant could not be reached. Check your connection and try again.",
                ex);
        }

        return codeBuilder.ToString();
    }

    // ── Myers diff (line-level) ─────────────────────────────────────────────────

    /// <summary>
    /// Computes a line-level diff between <paramref name="beforeCode"/> and <paramref name="afterCode"/>
    /// using a simple LCS-based algorithm. Returns a <see cref="DiffResult"/> where each line is
    /// tagged as Unchanged, Added, or Removed, with <c>IsContext = false</c> for all lines
    /// (no context windowing — use <see cref="IWorkflowCodeDiffService"/> for windowed diffs).
    /// </summary>
    private static DiffResult ComputeLinesDiff(string beforeCode, string afterCode)
    {
        var beforeLines = beforeCode.Split('\n');
        var afterLines  = afterCode.Split('\n');

        var lcsMatrix = BuildLcsMatrix(beforeLines, afterLines);
        var diffLines = new List<DiffLine>();

        // Backtrack through the LCS matrix to produce the diff.
        TraceBack(lcsMatrix, beforeLines, afterLines, beforeLines.Length, afterLines.Length, diffLines);

        var addedCount   = diffLines.Count(line => line.Type == DiffLineType.Added);
        var removedCount = diffLines.Count(line => line.Type == DiffLineType.Removed);

        return new DiffResult(
            diffLines.AsReadOnly(),
            HasChanges: addedCount > 0 || removedCount > 0,
            AddedCount: addedCount,
            RemovedCount: removedCount);
    }

    private static int[,] BuildLcsMatrix(string[] before, string[] after)
    {
        var rows    = before.Length + 1;
        var columns = after.Length + 1;
        var matrix  = new int[rows, columns];

        for (var rowIndex = 1; rowIndex < rows; rowIndex++)
        {
            for (var colIndex = 1; colIndex < columns; colIndex++)
            {
                matrix[rowIndex, colIndex] = before[rowIndex - 1] == after[colIndex - 1]
                    ? matrix[rowIndex - 1, colIndex - 1] + 1
                    : Math.Max(matrix[rowIndex - 1, colIndex], matrix[rowIndex, colIndex - 1]);
            }
        }

        return matrix;
    }

    private static void TraceBack(
        int[,] matrix,
        string[] before,
        string[] after,
        int beforeIndex,
        int afterIndex,
        List<DiffLine> result)
    {
        if (beforeIndex == 0 && afterIndex == 0)
            return;

        if (beforeIndex > 0 && afterIndex > 0 && before[beforeIndex - 1] == after[afterIndex - 1])
        {
            TraceBack(matrix, before, after, beforeIndex - 1, afterIndex - 1, result);
            result.Add(new DiffLine(before[beforeIndex - 1], DiffLineType.Unchanged, IsContext: false));
        }
        else if (afterIndex > 0 && (beforeIndex == 0 || matrix[beforeIndex, afterIndex - 1] >= matrix[beforeIndex - 1, afterIndex]))
        {
            TraceBack(matrix, before, after, beforeIndex, afterIndex - 1, result);
            result.Add(new DiffLine(after[afterIndex - 1], DiffLineType.Added, IsContext: false));
        }
        else
        {
            TraceBack(matrix, before, after, beforeIndex - 1, afterIndex, result);
            result.Add(new DiffLine(before[beforeIndex - 1], DiffLineType.Removed, IsContext: false));
        }
    }
}
