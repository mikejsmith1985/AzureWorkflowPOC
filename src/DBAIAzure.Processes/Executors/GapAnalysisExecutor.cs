// MAF executor that generates clarifying questions for a not-ready ticket (spec-019 T017) — the GA
// replacement for the SK GapAnalysisStep. Same prompt/parse; forwards to the HITL gate (parity — FR-015).
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Asks the model for 2–3 clarifying questions on a ticket that failed the Definition of Ready, attaches
/// them to the ticket, and forwards it to the human-in-the-loop gate. Mirrors the retired
/// <c>GapAnalysisStep</c>.
/// </summary>
[SendsMessage(typeof(TicketState))]
public sealed class GapAnalysisExecutor : Executor<TicketState>
{
    private readonly IChatClient _chatClient;
    private readonly IProgressReporter? _reporter;

    /// <summary>Creates the gap-analysis executor over the provider-neutral model client and optional progress sink.</summary>
    public GapAnalysisExecutor(IChatClient chatClient, IProgressReporter? reporter = null)
        : base(MafExecutorIds.GapAnalysis)
    {
        _chatClient = chatClient;
        _reporter = reporter;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(TicketState ticket, IWorkflowContext context, CancellationToken cancellationToken)
    {
        _reporter?.ReportStep("GapAnalysis", "Generating clarifying questions...");

        var prompt = $"""
            You are a scrum master. A ticket has failed the Definition of Ready check.
            Generate exactly 2-3 short, specific clarifying questions that a Product Owner
            must answer before the ticket can be estimated.

            Focus only on what is missing - do NOT ask about things already covered.

            Return a JSON array of strings (the questions only, no numbering):
            ["question 1", "question 2"]

            Ticket title: {ticket.Title}
            Ticket description: {ticket.Description}
            """;

        var resultText = await ExecutorLlm.CompleteStreamingAsync(_chatClient, prompt, "GapAnalysis", _reporter, cancellationToken);
        var json = ExecutorLlm.StripCodeFences(resultText);

        var questions = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        var updated = ticket with { ClarifyingQuestions = questions };

        _reporter?.ReportSnapshot("GapAnalysis", ticket, updated);
        foreach (var question in questions)
        {
            _reporter?.ReportStep("GapAnalysis", question, ReportLevel.Warning);
        }

        await context.SendMessageAsync(updated, cancellationToken);
    }
}
