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
    public async Task<(string UpdatedCode, CodeDiff Diff)> RefineAsync(
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
        var history  = new ChatHistory();
        history.AddSystemMessage(BuildSystemPrompt(topology));

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

    private static string BuildSystemPrompt(string topology) =>
        $"""
        You are an expert Semantic Kernel developer. Generate production-ready C# code
        implementing the workflow described below as a KernelProcess with typed steps and events.
        Follow .NET 8 and Semantic Kernel 1.x conventions. Add XML doc comments to every public type.

        {topology}
        """;

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
    /// using a simple LCS-based algorithm. Returns a <see cref="CodeDiff"/> where each line is
    /// tagged as Unchanged, Added, or Removed.
    /// </summary>
    private static CodeDiff ComputeLinesDiff(string beforeCode, string afterCode)
    {
        var beforeLines = beforeCode.Split('\n');
        var afterLines  = afterCode.Split('\n');

        var lcsMatrix = BuildLcsMatrix(beforeLines, afterLines);
        var diffLines = new List<DiffLine>();
        int lineNumber = 1;

        // Backtrack through the LCS matrix to produce the diff.
        TraceBack(lcsMatrix, beforeLines, afterLines, beforeLines.Length, afterLines.Length,
            diffLines, ref lineNumber);

        return new CodeDiff(diffLines.AsReadOnly());
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
        List<DiffLine> result,
        ref int lineNumber)
    {
        if (beforeIndex == 0 && afterIndex == 0)
            return;

        if (beforeIndex > 0 && afterIndex > 0 && before[beforeIndex - 1] == after[afterIndex - 1])
        {
            TraceBack(matrix, before, after, beforeIndex - 1, afterIndex - 1, result, ref lineNumber);
            result.Add(new DiffLine(lineNumber++, DiffLineKind.Unchanged, before[beforeIndex - 1]));
        }
        else if (afterIndex > 0 && (beforeIndex == 0 || matrix[beforeIndex, afterIndex - 1] >= matrix[beforeIndex - 1, afterIndex]))
        {
            TraceBack(matrix, before, after, beforeIndex, afterIndex - 1, result, ref lineNumber);
            result.Add(new DiffLine(lineNumber++, DiffLineKind.Added, after[afterIndex - 1]));
        }
        else
        {
            TraceBack(matrix, before, after, beforeIndex - 1, afterIndex, result, ref lineNumber);
            result.Add(new DiffLine(lineNumber++, DiffLineKind.Removed, before[beforeIndex - 1]));
        }
    }
}
