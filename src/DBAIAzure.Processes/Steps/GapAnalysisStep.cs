using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace DBAIAzure.Processes.Steps;

public class GapAnalysisStep : KernelProcessStep
{
    [KernelFunction]
    public async Task GenerateQuestionsAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel kernel)
    {
        var prompt = $"""
            You are a scrum master. A ticket has failed the Definition of Ready check.
            Generate exactly 2-3 short, specific clarifying questions that a Product Owner
            must answer before the ticket can be estimated.

            Focus only on what is missing — do NOT ask about things already covered.

            Return a JSON array of strings (the questions only, no numbering):
            ["question 1", "question 2"]

            Ticket title: {ticket.Title}
            Ticket description: {ticket.Description}
            """;

        var resultText = (await kernel.InvokePromptAsync(prompt)).ToString();

        var json = resultText.Trim();
        if (json.StartsWith("```"))
        {
            json = string.Join('\n', json.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")));
        }

        var questions = JsonSerializer.Deserialize<List<string>>(json) ?? [];

        var updated = ticket with { ClarifyingQuestions = questions };
        await ctx.EmitEventAsync(new() { Id = Events.QuestionsReady, Data = updated });
    }
}
