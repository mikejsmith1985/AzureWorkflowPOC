// MAF executor for the Definition-of-Ready check (spec-019 T017) — the GA replacement for the SK
// ValidationStep. Same prompt/parse, and the same three-way routing: ready → estimate, blocked (max
// rounds) → terminal, not-ready → gap analysis (parity — FR-015).
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Evaluates the ticket against the Definition of Ready and routes accordingly: a ready ticket goes to
/// the estimation executor; a ticket that has exhausted its clarification rounds is yielded as a
/// terminal (blocked) output; otherwise the ticket — tagged with the missing fields as clarifying
/// questions — goes to the gap-analysis executor. Mirrors the retired <c>ValidationStep</c>.
/// </summary>
[SendsMessage(typeof(TicketState))]
[YieldsOutput(typeof(TicketState))]
public sealed class ValidationExecutor : Executor<TicketState>
{
    /// <summary>Clarification rounds allowed before a not-ready ticket is blocked (parity with the SK step).</summary>
    private const int MaxClarificationRounds = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;
    private readonly IProgressReporter? _reporter;

    /// <summary>Creates the validation executor over the provider-neutral model client and optional progress sink.</summary>
    public ValidationExecutor(IChatClient chatClient, IProgressReporter? reporter = null)
        : base(MafExecutorIds.Validation)
    {
        _chatClient = chatClient;
        _reporter = reporter;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(TicketState ticket, IWorkflowContext context, CancellationToken cancellationToken)
    {
        _reporter?.ReportStep("Validation", "Running DoR check...");

        var clarification = ticket.HumanAnswer is not null
            ? $"\nPO clarification (round {ticket.ClarificationRound}): {ticket.HumanAnswer}"
            : string.Empty;

        var prompt = $$"""
            You are a scrum master evaluating a ticket against the Definition of Ready (DoR).
            A ticket is READY when it has:
              1. A clear, unambiguous title
              2. Acceptance criteria (what "done" looks like)
              3. At least one concrete technical detail (API, table, endpoint, etc.)
              4. Enough context to estimate effort without follow-up questions

            Return ONLY a raw JSON object with these EXACT keys (no markdown, no code fences):
            {"is_ready": true, "missing_fields": [], "reasoning": "one-sentence explanation"}

            Ticket title: {{ticket.Title}}
            Ticket description: {{ticket.Description}}{{clarification}}
            """;

        var resultText = await ExecutorLlm.CompleteStreamingAsync(_chatClient, prompt, "Validation", _reporter, cancellationToken);
        var json = ExecutorLlm.StripCodeFences(resultText);

        var verdict = JsonSerializer.Deserialize<DorVerdict>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse DorVerdict from: {json}");

        if (verdict.IsReady)
        {
            _reporter?.ReportSnapshot("Validation", ticket, ticket);
            _reporter?.ReportStep("Validation", $"Ready - {verdict.Reasoning}", ReportLevel.Success);
            await context.SendMessageAsync(ticket, cancellationToken);
        }
        else if (ticket.ClarificationRound >= MaxClarificationRounds)
        {
            // Blocked: no downstream — surface the ticket as the run's terminal output (parity with the
            // SK Blocked event, which had no onward edge and ended the process).
            _reporter?.ReportStep("Validation", "Blocked - max clarification rounds reached", ReportLevel.Error);
            await context.YieldOutputAsync(ticket, cancellationToken);
        }
        else
        {
            var withQuestions = ticket with { ClarifyingQuestions = verdict.MissingFields ?? [] };
            _reporter?.ReportSnapshot("Validation", ticket, withQuestions);
            _reporter?.ReportStep("Validation", "Not Ready", ReportLevel.Warning);
            await context.SendMessageAsync(withQuestions, cancellationToken);
        }
    }
}
