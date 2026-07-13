// MAF executor that estimates effort in Fibonacci points (spec-019 T017) — the GA replacement for the SK
// EstimationStep. Same anchor prompt, same nearest-valid-point snap; forwards to Action (parity — FR-015).
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Estimates the ticket's effort against Fibonacci anchor tasks, snaps the model's number to the nearest
/// valid point value, records the reasoning, and forwards the ticket to the action executor. Mirrors the
/// retired <c>EstimationStep</c>.
/// </summary>
[SendsMessage(typeof(TicketState))]
public sealed class EstimationExecutor : Executor<TicketState>
{
    private const string AnchorTable = """
        1  - Add a null check or log statement
        2  - Add a new field to an existing model + migration
        3  - Implement a single new REST endpoint with tests
        5  - Build a new CRUD feature with validation logic
        8  - Build a new integration with an external system
        13 - Refactor a core subsystem or migrate a database schema
        21 - Architect a new major feature spanning multiple services
        """;

    private static readonly int[] ValidPoints = [1, 2, 3, 5, 8, 13, 21];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;
    private readonly IProgressReporter? _reporter;

    /// <summary>Creates the estimation executor over the provider-neutral model client and optional progress sink.</summary>
    public EstimationExecutor(IChatClient chatClient, IProgressReporter? reporter = null)
        : base(MafExecutorIds.Estimation)
    {
        _chatClient = chatClient;
        _reporter = reporter;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(TicketState ticket, IWorkflowContext context, CancellationToken cancellationToken)
    {
        _reporter?.ReportStep("Estimation", "Estimating effort via Fibonacci anchors...");

        var clarification = ticket.HumanAnswer is not null
            ? $"\nPO clarification: {ticket.HumanAnswer}"
            : string.Empty;

        var prompt = $$"""
            You are an experienced engineering lead using reference class forecasting.
            Estimate effort using ONLY the Fibonacci values 1, 2, 3, 5, 8, 13, 21.
            Compare the ticket against the anchor tasks below and pick the closest match.

            Anchor tasks:
            {{AnchorTable}}

            Return ONLY a raw JSON object with these EXACT keys (no markdown, no code fences):
            {"points": 3, "reasoning": "one sentence referencing which anchor this compares to and why"}

            Ticket title: {{ticket.Title}}
            Ticket description: {{ticket.Description}}{{clarification}}
            """;

        var resultText = await ExecutorLlm.CompleteStreamingAsync(_chatClient, prompt, "Estimation", _reporter, cancellationToken);
        var json = ExecutorLlm.StripCodeFences(resultText);

        var result = JsonSerializer.Deserialize<EstimationResult>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse EstimationResult: {json}");

        var points = ValidPoints.MinBy(candidate => Math.Abs(candidate - result.Points));
        var updated = ticket with { StoryPoints = points, EstimationReasoning = result.Reasoning };

        _reporter?.ReportSnapshot("Estimation", ticket, updated);
        _reporter?.ReportStep("Estimation", $"{points} pts - {result.Reasoning}", ReportLevel.Success);

        await context.SendMessageAsync(updated, cancellationToken);
    }
}
