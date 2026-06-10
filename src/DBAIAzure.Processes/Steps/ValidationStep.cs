using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Processes.Steps;

public class ValidationStep : KernelProcessStep
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [KernelFunction]
    public async Task ValidateAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel kernel)
    {
        var reporter    = kernel.Services.GetService<IProgressReporter>();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        reporter?.ReportStep("Validation", "Running DoR check...");

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

        var history = new ChatHistory();
        history.AddUserMessage(prompt);

        var fullText = new StringBuilder();
        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(history, kernel: kernel))
        {
            var token = chunk.Content ?? string.Empty;
            fullText.Append(token);
            reporter?.ReportToken("Validation", token);
        }
        var resultText = fullText.ToString().Trim();

        var json = resultText.StartsWith("```")
            ? string.Join('\n', resultText.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")))
            : resultText;

        var verdict = JsonSerializer.Deserialize<DorVerdict>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Could not parse DorVerdict from: {json}");

        if (verdict.IsReady)
        {
            reporter?.ReportSnapshot("Validation", ticket, ticket);
            reporter?.ReportStep("Validation", $"Ready - {verdict.Reasoning}", ReportLevel.Success);
            AnsiConsole.MarkupLine($"  [green]DoR: Ready[/] - {Markup.Escape(verdict.Reasoning ?? string.Empty)}");
            await ctx.EmitEventAsync(new() { Id = Events.ReadyPath, Data = ticket });
        }
        else if (ticket.ClarificationRound >= 3)
        {
            reporter?.ReportStep("Validation", "Blocked - max clarification rounds reached", ReportLevel.Error);
            AnsiConsole.MarkupLine("  [red]DoR: Blocked[/] - max clarification rounds reached");
            await ctx.EmitEventAsync(new() { Id = Events.Blocked, Data = ticket });
        }
        else
        {
            var missingList = verdict.MissingFields?.Count > 0
                ? string.Join(", ", verdict.MissingFields)
                : "unspecified gaps";
            var withQuestions = ticket with { ClarifyingQuestions = verdict.MissingFields ?? [] };
            reporter?.ReportSnapshot("Validation", ticket, withQuestions);
            reporter?.ReportStep("Validation", $"Not Ready - missing: {missingList}", ReportLevel.Warning);
            AnsiConsole.MarkupLine($"  [yellow]DoR: Not Ready[/] - missing: {Markup.Escape(missingList)}");
            await ctx.EmitEventAsync(new() { Id = Events.NotReadyPath, Data = ticket });
        }
    }
}
