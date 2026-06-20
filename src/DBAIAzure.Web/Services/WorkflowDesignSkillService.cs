// WorkflowDesignSkillService — Semantic Kernel plugin that conducts a structured
// design-review conversation before code generation, ensuring the LLM has enough
// context about each node's intended behaviour.

#pragma warning disable SKEXP0001

using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;

namespace DBAIAzure.Web.Services;

/// <summary>
/// A Semantic Kernel plugin that analyses a workflow topology and generates clarifying
/// questions for the user, one question at a time. Answers are persisted in
/// <see cref="WorkflowSettings.DesignSkillAnswers"/> so the same question is never
/// shown again. A deferred question is recorded as <c>"user-deferred"</c> so the assistant
/// can proceed without blocking on optional decisions.
/// </summary>
public sealed class WorkflowDesignSkillService
{
    private const string UserDeferredSentinel = "user-deferred";

    private readonly WorkflowTopologySerializer _serializer;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<WorkflowDesignSkillService> _logger;

    /// <summary>
    /// Initialises the service with the topology serializer and SK chat completion service.
    /// </summary>
    public WorkflowDesignSkillService(
        WorkflowTopologySerializer serializer,
        IChatCompletionService chatService,
        ILogger<WorkflowDesignSkillService> logger)
    {
        _serializer  = serializer;
        _chatService = chatService;
        _logger      = logger;
    }

    /// <summary>
    /// Analyses the workflow topology and returns questions that have not yet been answered
    /// or deferred by the user. The question keys are derived from node IDs so they are stable
    /// across renames and reloads.
    /// </summary>
    /// <param name="workflow">The current workflow definition.</param>
    /// <returns>
    /// A list of (QuestionKey, QuestionText) pairs for all pending design questions.
    /// Empty when every question has already been answered or deferred.
    /// </returns>
    public async Task<IReadOnlyList<(string Key, string Text)>> GetPendingQuestionsAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        var allQuestions = await AnalyseTopologyAsync(workflow, cancellationToken).ConfigureAwait(false);
        var pendingQuestions = allQuestions
            .Where(q => !workflow.Settings.DesignSkillAnswers.ContainsKey(q.Key))
            .ToList()
            .AsReadOnly();
        return pendingQuestions;
    }

    /// <summary>
    /// Records the user's answer for the given question key, returning an updated settings snapshot.
    /// </summary>
    public WorkflowSettings RecordAnswer(WorkflowSettings settings, string questionKey, string answer)
    {
        var updatedAnswers = new Dictionary<string, string>(settings.DesignSkillAnswers)
        {
            [questionKey] = answer
        };
        return settings with { DesignSkillAnswers = updatedAnswers.AsReadOnly() };
    }

    /// <summary>
    /// Defers a question without an answer, recording the sentinel value so it is not re-asked.
    /// The assistant proceeds normally after deferral — no blocking.
    /// </summary>
    public WorkflowSettings DeferQuestion(WorkflowSettings settings, string questionKey)
    {
        return RecordAnswer(settings, questionKey, UserDeferredSentinel);
    }

    // ── KernelFunction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Semantic Kernel plugin function — exposed to the kernel tool loop.
    /// Calls the LLM with the serialised topology to produce a structured list of design questions.
    /// Returns a list of (Key, Text) pairs; each key is stable across renames.
    /// </summary>
    [KernelFunction("AnalyseTopology")]
    [Description("Analyses a workflow topology and returns a list of clarifying design questions.")]
    public async Task<IReadOnlyList<(string Key, string Text)>> AnalyseTopologyAsync(
        [Description("The workflow definition to analyse.")] WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        var topology = _serializer.Serialize(workflow);
        var history  = new ChatHistory();
        history.AddSystemMessage(BuildSystemPrompt());
        history.AddUserMessage(topology);

        try
        {
            var response = await _chatService
                .GetChatMessageContentAsync(history, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ParseQuestionsFromResponse(workflow, response.Content ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM question generation failed; returning per-node defaults.");
            return BuildDefaultQuestions(workflow);
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private static string BuildSystemPrompt() =>
        """
        You are a workflow design consultant reviewing an agentic pipeline.
        Given the workflow topology below, output one clarifying question per step
        that will help code generation produce correct, production-ready Semantic Kernel steps.
        Format each question on its own line, prefixed by the step number: "1. <question>"
        Keep each question to one sentence and avoid technical jargon.
        """;

    private static IReadOnlyList<(string Key, string Text)> ParseQuestionsFromResponse(
        WorkflowDefinition workflow,
        string responseContent)
    {
        var lines = responseContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 2 && char.IsDigit(line[0]))
            .ToList();

        var results = new List<(string Key, string Text)>();
        for (var index = 0; index < lines.Count && index < workflow.Nodes.Count; index++)
        {
            // Strip leading "N. " prefix.
            var questionText = lines[index].Length > 3 ? lines[index][3..].Trim() : lines[index];
            var nodeId = workflow.Nodes[index].Id;
            results.Add(($"node.{nodeId}.design", questionText));
        }

        return results.AsReadOnly();
    }

    private static IReadOnlyList<(string Key, string Text)> BuildDefaultQuestions(
        WorkflowDefinition workflow)
    {
        return workflow.Nodes
            .Select(node => (
                Key : $"node.{node.Id}.design",
                Text: $"What should happen when '{node.Label}' completes?"))
            .ToList()
            .AsReadOnly();
    }
}
