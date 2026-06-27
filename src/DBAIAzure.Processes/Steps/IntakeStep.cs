using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Processes.Steps;

public class IntakeStep : KernelProcessStep
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [KernelFunction]
    public async Task NormalizeAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel kernel)
    {
        var reporter    = kernel.Services.GetService<IProgressReporter>();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        reporter?.ReportStep("Intake", $"Normalizing: {ticket.Title}");

        var prompt = $$"""
            Normalize the following support ticket for Definition of Ready validation.
            Return ONLY a raw JSON object with these keys (no markdown, no code fences):
            {"title": "concise active-voice title up to 10 words", "description": "technical detail preserved, filler removed"}

            Original title: {{ticket.Title}}
            Original description: {{ticket.Description}}
            """;

        var history = new ChatHistory();
        history.AddUserMessage(prompt);

        var fullText = new StringBuilder();
        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(history, kernel: kernel))
        {
            var token = chunk.Content ?? string.Empty;
            fullText.Append(token);
            reporter?.ReportToken("Intake", token);
        }
        var resultText = fullText.ToString().Trim();

        var json = resultText.StartsWith("```")
            ? string.Join('\n', resultText.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")))
            : resultText;

        NormalizedTicket? normalized = null;
        try { normalized = JsonSerializer.Deserialize<NormalizedTicket>(json, JsonOpts); }
        catch { /* keep original values on parse failure */ }

        var updated = ticket with
        {
            Title       = normalized?.Title ?? ticket.Title,
            Description = normalized?.Description ?? ticket.Description,
        };

        reporter?.ReportSnapshot("Intake", ticket, updated);
        reporter?.ReportStep("Intake", $"Normalized: {updated.Title}", ReportLevel.Success);
        AnsiConsole.MarkupLine($"  [dim]Intake:[/] {Markup.Escape(updated.Title)}");

        await ctx.EmitEventAsync(new() { Id = Events.IntakeComplete, Data = updated });
    }

    private record NormalizedTicket(string? Title, string? Description);
}
