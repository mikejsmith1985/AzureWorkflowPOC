using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace DBAIAzure.Processes.Steps;

public class ValidationStep : KernelProcessStep
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [KernelFunction]
    public async Task ValidateAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel kernel)
    {
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

        var resultText = (await kernel.InvokePromptAsync(prompt)).ToString().Trim();

        var json = resultText.StartsWith("```")
            ? string.Join('\n', resultText.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")))
            : resultText;

        var verdict = JsonSerializer.Deserialize<DorVerdict>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Could not parse DorVerdict from: {json}");

        if (verdict.IsReady)
        {
            await ctx.EmitEventAsync(new() { Id = Events.ReadyPath, Data = ticket });
        }
        else if (ticket.ClarificationRound >= 3)
        {
            await ctx.EmitEventAsync(new() { Id = Events.Blocked, Data = ticket });
        }
        else
        {
            await ctx.EmitEventAsync(new() { Id = Events.NotReadyPath, Data = ticket });
        }
    }
}
