using DBAIAzure.Core.Models;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Bridges IProgressReporter (called by SK process steps) to a live PipelineRun.
/// One instance per run — created by PipelineOrchestrator before each RunToEndAsync call.
/// </summary>
public sealed class BoundProgressReporter : IProgressReporter
{
    private readonly PipelineRun _run;
    private readonly Action _notifyUpdated;

    /// <summary>Populated by ReportComplete; read by the orchestrator after RunToEndAsync returns.</summary>
    public TicketState? FinalTicket { get; private set; }

    public BoundProgressReporter(PipelineRun run, Action notifyUpdated)
    {
        _run = run;
        _notifyUpdated = notifyUpdated;
    }

    public void ReportStep(string stepName, string message, ReportLevel level = ReportLevel.Info)
    {
        _run.AddEvent(new PipelineEvent(stepName, message, level, DateTimeOffset.UtcNow));
        _notifyUpdated();
    }

    public void ReportComplete(TicketState finalState)
    {
        FinalTicket = finalState;
        var jiraLine = finalState.JiraIssueUrl is not null
            ? $"Jira issue created: {finalState.JiraIssueUrl}"
            : "Pipeline complete";
        _run.AddEvent(new PipelineEvent("Action", jiraLine, ReportLevel.Success, DateTimeOffset.UtcNow));
        _notifyUpdated();
    }
}
