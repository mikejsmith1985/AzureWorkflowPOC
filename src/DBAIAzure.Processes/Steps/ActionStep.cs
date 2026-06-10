using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using Spectre.Console;

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

        // Summary panel — the key demo output for the happy path
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[dim]Field[/]")
            .AddColumn("[bold]Value[/]")
            .AddRow("Ticket ID", Markup.Escape(updated.TicketId))
            .AddRow("Title", Markup.Escape(updated.Title))
            .AddRow("Story Points", updated.StoryPoints?.ToString() ?? "[dim]—[/]")
            .AddRow("Reasoning", Markup.Escape(updated.EstimationReasoning ?? "—"))
            .AddRow("Jira URL", $"[link={Markup.Escape(url)}]{Markup.Escape(url)}[/]");

        AnsiConsole.Write(table);

        // No further routing — action is the terminal node on the ready path
        await ctx.EmitEventAsync(new() { Id = "ProcessComplete", Data = updated });
    }
}
