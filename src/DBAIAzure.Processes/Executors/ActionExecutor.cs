// MAF executor that records the created work item and completes the run (spec-019 T017) — the GA
// replacement for the SK ActionStep. Same mock-issue behaviour; yields the final ticket (parity — FR-015).
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Terminal executor of the intake pipeline: assigns the (mock) issue URL and yields the finished ticket
/// as the workflow output. Mirrors the retired <c>ActionStep</c>, which likewise created a placeholder
/// issue rather than calling an external tracker on the intake path.
/// </summary>
[YieldsOutput(typeof(TicketState))]
public sealed class ActionExecutor : Executor<TicketState>
{
    // Keeps the placeholder key in the same small range the SK step used, so the mock URL is stable.
    private const int IssueKeySpan = 900;
    private const int IssueKeyBase = 100;

    private readonly IProgressReporter? _reporter;

    /// <summary>Creates the action executor with an optional progress sink (it makes no model call).</summary>
    public ActionExecutor(IProgressReporter? reporter = null)
        : base(MafExecutorIds.Action)
    {
        _reporter = reporter;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(TicketState ticket, IWorkflowContext context, CancellationToken cancellationToken)
    {
        _reporter?.ReportStep("Action", "Creating Jira issue...");

        var issueKey = $"SBRO-{Math.Abs(ticket.TicketId.GetHashCode()) % IssueKeySpan + IssueKeyBase}";
        var url = $"https://jira.example.com/browse/{issueKey}";
        var updated = ticket with { JiraIssueUrl = url };

        _reporter?.ReportSnapshot("Action", ticket, updated);
        _reporter?.ReportComplete(updated);

        await context.YieldOutputAsync(updated, cancellationToken);
    }
}
