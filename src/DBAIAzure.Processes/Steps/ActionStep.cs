using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

public class ActionStep : KernelProcessStep
{
    [KernelFunction]
    public async Task CreateJiraAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel _)
    {
        // Mock Jira creation — in production this would call IActionConnector
        var issueKey = $"SBRO-{Math.Abs(ticket.TicketId.GetHashCode()) % 900 + 100}";
        var url = $"https://jira.example.com/browse/{issueKey}";

        var updated = ticket with { JiraIssueUrl = url };

        // No further routing — action is the terminal node on the ready path
        await ctx.EmitEventAsync(new() { Id = "ProcessComplete", Data = updated });
    }
}
