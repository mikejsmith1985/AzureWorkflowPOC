using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using Spectre.Console;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Emits an external (public) event that suspends the process until the runner
/// injects a HumanResponded event — the .NET equivalent of LangGraph's interrupt/resume.
///
/// Interview talking point: SK Process Framework's external event mechanism gives you
/// HITL suspend/resume without a Python async context manager or custom checkpoint store.
/// </summary>
public class HitlPauseStep : KernelProcessStep
{
    [KernelFunction]
    public async Task PauseForHumanAsync(KernelProcessStepContext ctx, TicketState ticket, Kernel _)
    {
        // Print questions so the PO knows what to answer when the runner resumes the process.
        // The HITL resume path (Day 2) will collect Console.ReadLine() here and call
        // process.SendMessageAsync(Events.HumanResponded, updatedTicket).
        AnsiConsole.MarkupLine("  [bold yellow]⏸ HITL pause — awaiting PO input[/]");
        AnsiConsole.MarkupLine("  [dim]Resume path (Day 2): runner will inject HumanResponded → ValidationStep re-runs[/]");

        // Emit a PUBLIC event so the outer runner can subscribe to it.
        // The process suspends here; the runner reads Console.ReadLine() and then
        // calls process.SendMessageAsync(Events.HumanResponded, updatedTicket) to resume.
        await ctx.EmitEventAsync(new()
        {
            Id = Events.AwaitHuman,
            Data = ticket,
        });
    }
}
