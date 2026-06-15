using DBAIAzure.Core.Models;
using System.Text.Json;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Bridges IProgressReporter (called by SK process steps) to a live PipelineRun and
/// the optional persistence layer (IRunRepository).
/// One instance created per run per clarification round.
/// </summary>
public sealed class BoundProgressReporter : IProgressReporter
{
    private readonly PipelineRun _run;
    private readonly Action _notifyUpdated;
    private readonly IRunRepository _repository;
    private static readonly JsonSerializerOptions SerializerOpts = new() { WriteIndented = true };

    /// <summary>Populated by ReportComplete; read by the orchestrator after RunToEndAsync returns.</summary>
    public TicketState? FinalTicket { get; private set; }

    public BoundProgressReporter(PipelineRun run, Action notifyUpdated, IRunRepository? repository = null)
    {
        _run = run;
        _notifyUpdated = notifyUpdated;
        _repository = repository ?? NullRunRepository.Instance;
    }

    public void ReportStep(string stepName, string message, ReportLevel level = ReportLevel.Info)
    {
        _run.AddEvent(new PipelineEvent(stepName, message, level, DateTimeOffset.UtcNow));
        _notifyUpdated();
    }

    public void ReportToken(string stepName, string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        _run.AddToken(new StreamToken(stepName, token, DateTimeOffset.UtcNow));
        _notifyUpdated();
    }

    public void ReportSnapshot(string stepName, TicketState before, TicketState after)
    {
        var snapshot = new StepStateSnapshot(
            stepName,
            JsonSerializer.Serialize(before, SerializerOpts),
            JsonSerializer.Serialize(after, SerializerOpts),
            DateTimeOffset.UtcNow);

        _run.AddSnapshot(snapshot);

        // Persist asynchronously — fire-and-forget; failures are non-blocking
        _ = _repository.AddSnapshotAsync(_run.RunId, stepName, before, after);
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
