// WorkflowDesignSkillService — design review and whole-workflow generation over the provider-neutral chat client.

using DBAIAzure.Core.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Analyses a workflow topology and generates clarifying questions for the user, one question at a
/// time. Answers are persisted in
/// <see cref="WorkflowSettings.DesignSkillAnswers"/> so the same question is never
/// shown again. A deferred question is recorded as <c>"user-deferred"</c> so the assistant
/// can proceed without blocking on optional decisions.
/// </summary>
public sealed class WorkflowDesignSkillService
{
    private const string UserDeferredSentinel = "user-deferred";

    private readonly WorkflowTopologySerializer _serializer;
    private readonly IChatClient _chatClient;
    private readonly ILogger<WorkflowDesignSkillService> _logger;

    /// <summary>
    /// Initialises the service with the topology serializer and the provider-neutral chat client.
    /// </summary>
    public WorkflowDesignSkillService(
        WorkflowTopologySerializer serializer,
        IChatClient chatClient,
        ILogger<WorkflowDesignSkillService> logger)
    {
        _serializer = serializer;
        _chatClient = chatClient;
        _logger     = logger;
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

    // ── Whole-workflow chat generation (FR-23, US6) ────────────────────────────

    /// <summary>
    /// Generates a complete workflow graph from a plain-language description.
    /// If the description is ambiguous, returns a <see cref="WorkflowGenerationResult"/> with
    /// <c>ClarifyingQuestion</c> set and empty node/edge lists — the caller must present the
    /// question to the user before re-invoking with a more specific description.
    /// </summary>
    public async Task<WorkflowGenerationResult> GenerateWorkflowAsync(
        string description,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, BuildGenerationSystemPrompt()),
            new(Microsoft.Extensions.AI.ChatRole.User, description),
        };

        try
        {
            var response = await _chatClient
                .GetResponseAsync(messages, options: null, ct)
                .ConfigureAwait(false);

            return ParseGenerationResponse(response.Text ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow generation failed for description: {Length} chars", description.Length);
            return new WorkflowGenerationResult(
                Nodes: Array.Empty<GeneratedNode>(),
                Edges: Array.Empty<GeneratedEdge>(),
                ClarifyingQuestion: "I had trouble generating the workflow. Could you describe what the workflow should do in one or two sentences?");
        }
    }

    private static string BuildGenerationSystemPrompt() =>
        """
        You are a workflow design assistant for an agentic pipeline builder.
        Given a plain-language description of a workflow, output a JSON object representing
        the workflow graph. Use ONLY these node types: Trigger, AgenticReason, FunctionRoute,
        FunctionTransform, FunctionNotify, FunctionData, HumanApproval.

        If the description is too vague or ambiguous to produce a reliable graph, output:
        {"clarifyingQuestion": "<one question>"}

        Otherwise output:
        {
          "nodes": [{"id": "n1", "nodeType": "Trigger", "label": "...", "goalPrompt": "..."}],
          "edges": [{"sourceNodeId": "n1", "targetNodeId": "n2"}]
        }

        Every workflow must start with exactly one Trigger node. Labels should be short and action-oriented.
        GoalPrompt should be a brief description of what the node achieves.
        Return valid JSON only — no markdown, no explanation.
        """;

    private static WorkflowGenerationResult ParseGenerationResponse(string content)
    {
        content = content.Trim();

        // Strip markdown code fences if the LLM wrapped the JSON.
        if (content.StartsWith("```"))
        {
            var firstNewLine = content.IndexOf('\n');
            var lastFence    = content.LastIndexOf("```");
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                content = content[(firstNewLine + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("clarifyingQuestion", out var cq))
                return new WorkflowGenerationResult(Array.Empty<GeneratedNode>(), Array.Empty<GeneratedEdge>(), cq.GetString());

            var nodes = new List<GeneratedNode>();
            if (doc.RootElement.TryGetProperty("nodes", out var nodesEl))
            {
                foreach (var n in nodesEl.EnumerateArray())
                {
                    nodes.Add(new GeneratedNode(
                        Id:         n.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N")[..4],
                        NodeType:   n.GetProperty("nodeType").GetString() ?? "AgenticReason",
                        Label:      n.TryGetProperty("label", out var l)      ? l.GetString() ?? string.Empty : string.Empty,
                        GoalPrompt: n.TryGetProperty("goalPrompt", out var gp) ? gp.GetString()                : null));
                }
            }

            var edges = new List<GeneratedEdge>();
            if (doc.RootElement.TryGetProperty("edges", out var edgesEl))
            {
                foreach (var e in edgesEl.EnumerateArray())
                {
                    edges.Add(new GeneratedEdge(
                        SourceNodeId: e.GetProperty("sourceNodeId").GetString() ?? string.Empty,
                        TargetNodeId: e.GetProperty("targetNodeId").GetString() ?? string.Empty));
                }
            }

            return new WorkflowGenerationResult(nodes.AsReadOnly(), edges.AsReadOnly(), null);
        }
        catch (JsonException)
        {
            return new WorkflowGenerationResult(
                Array.Empty<GeneratedNode>(),
                Array.Empty<GeneratedEdge>(),
                "I couldn't parse the workflow. Could you describe it differently?");
        }
    }

    // ── Topology analysis ────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the LLM with the serialised topology to produce a structured list of design questions.
    /// Returns a list of (Key, Text) pairs; each key is stable across renames.
    /// </summary>
    public async Task<IReadOnlyList<(string Key, string Text)>> AnalyseTopologyAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        var topology = _serializer.Serialize(workflow);
        var messages = new List<ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, BuildSystemPrompt()),
            new(Microsoft.Extensions.AI.ChatRole.User, topology),
        };

        try
        {
            var response = await _chatClient
                .GetResponseAsync(messages, options: null, cancellationToken)
                .ConfigureAwait(false);

            return ParseQuestionsFromResponse(workflow, response.Text ?? string.Empty);
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
        that will help code generation produce correct, production-ready pipeline steps.
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
