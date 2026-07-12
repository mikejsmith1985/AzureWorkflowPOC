// MAF executor that normalises an incoming ticket (spec-019 T017) — the GA replacement for the SK
// IntakeStep. Same prompt, same JSON parse, same fall-back-to-original behaviour (parity — FR-015).
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Normalises the ticket title/description for Definition-of-Ready validation, then forwards the updated
/// ticket to the validation executor. Mirrors the retired <c>IntakeStep</c>: identical prompt, tolerant
/// JSON parse, and keep-original-values-on-parse-failure semantics.
/// </summary>
[SendsMessage(typeof(TicketState))]
public sealed class IntakeExecutor : Executor<TicketState>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;
    private readonly IProgressReporter? _reporter;

    /// <summary>Creates the intake executor over the provider-neutral model client and optional progress sink.</summary>
    public IntakeExecutor(IChatClient chatClient, IProgressReporter? reporter = null)
        : base(MafExecutorIds.Intake)
    {
        _chatClient = chatClient;
        _reporter = reporter;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(TicketState ticket, IWorkflowContext context, CancellationToken cancellationToken)
    {
        _reporter?.ReportStep("Intake", $"Normalizing: {ticket.Title}");

        var prompt = $$"""
            Normalize the following support ticket for Definition of Ready validation.
            Return ONLY a raw JSON object with these keys (no markdown, no code fences):
            {"title": "concise active-voice title up to 10 words", "description": "technical detail preserved, filler removed"}

            Original title: {{ticket.Title}}
            Original description: {{ticket.Description}}
            """;

        var resultText = await ExecutorLlm.CompleteAsync(_chatClient, prompt, cancellationToken);
        var json = ExecutorLlm.StripCodeFences(resultText);

        NormalizedTicket? normalized = null;
        try { normalized = JsonSerializer.Deserialize<NormalizedTicket>(json, JsonOptions); }
        catch { /* keep original values on parse failure — identical to IntakeStep */ }

        var updated = ticket with
        {
            Title = normalized?.Title ?? ticket.Title,
            Description = normalized?.Description ?? ticket.Description,
        };

        _reporter?.ReportSnapshot("Intake", ticket, updated);
        _reporter?.ReportStep("Intake", $"Normalized: {updated.Title}", ReportLevel.Success);

        await context.SendMessageAsync(updated, cancellationToken);
    }

    private record NormalizedTicket(string? Title, string? Description);
}
