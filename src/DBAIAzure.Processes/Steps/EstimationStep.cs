using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Processes.Steps;

public class EstimationStep : KernelProcessStep
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
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [KernelFunction]
    public async Task EstimateAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel kernel)
    {
        var reporter    = kernel.Services.GetService<IProgressReporter>();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        reporter?.ReportStep("Estimation", "Estimating effort via Fibonacci anchors...");

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

        var history = new ChatHistory();
        history.AddUserMessage(prompt);

        var fullText = new StringBuilder();
        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(history, kernel: kernel))
        {
            var token = chunk.Content ?? string.Empty;
            fullText.Append(token);
            reporter?.ReportToken("Estimation", token);
        }
        var resultText = fullText.ToString().Trim();

        var json = resultText.StartsWith("```")
            ? string.Join('\n', resultText.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")))
            : resultText;

        var result = JsonSerializer.Deserialize<EstimationResult>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Could not parse EstimationResult: {json}");

        var points  = ValidPoints.MinBy(p => Math.Abs(p - result.Points));
        var updated = ticket with { StoryPoints = points, EstimationReasoning = result.Reasoning };

        reporter?.ReportSnapshot("Estimation", ticket, updated);
        reporter?.ReportStep("Estimation", $"{points} pts - {result.Reasoning}", ReportLevel.Success);
        AnsiConsole.MarkupLine($"  [cyan]Estimate:[/] [bold]{points}[/] pts - {Markup.Escape(result.Reasoning ?? string.Empty)}");

        await ctx.EmitEventAsync(new() { Id = Events.EstimationComplete, Data = updated });
    }
}
