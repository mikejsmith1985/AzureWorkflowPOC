using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Spectre.Console;

namespace DBAIAzure.Processes.Steps;

public class ActionStep : KernelProcessStep
{
    [KernelFunction]
    public async Task CreateJiraAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel kernel)
    {
        var reporter = kernel.Services.GetService<IProgressReporter>();
        reporter?.ReportStep("Action", "Creating Jira issue...");

        // Mock Jira creation - replace with IActionConnector in Phase 3
        var issueKey = $"SBRO-{Math.Abs(ticket.TicketId.GetHashCode()) % 900 + 100}";
        var url      = $"https://jira.example.com/browse/{issueKey}";
        var updated  = ticket with { JiraIssueUrl = url };

        reporter?.ReportSnapshot("Action", ticket, updated);
        reporter?.ReportComplete(updated);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[dim]Field[/]")
            .AddColumn("[bold]Value[/]")
            .AddRow("Ticket ID",    Markup.Escape(updated.TicketId))
            .AddRow("Title",        Markup.Escape(updated.Title))
            .AddRow("Story Points", updated.StoryPoints?.ToString() ?? "[dim]-[/]")
            .AddRow("Reasoning",    Markup.Escape(updated.EstimationReasoning ?? "-"))
            .AddRow("Jira URL",     $"[link={Markup.Escape(url)}]{Markup.Escape(url)}[/]");
        AnsiConsole.Write(table);

        await ctx.EmitEventAsync(new() { Id = "ProcessComplete", Data = updated });
    }
}
