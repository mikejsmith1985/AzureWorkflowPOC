using DBAIAzure.Processes.Pipeline;

namespace DBAIAzure.Storage.Entities;

/// <summary>
/// Persisted representation of a single pipeline run.
/// Scalars are mapped to columns for filtering; the full TicketState is stored as JSON.
/// </summary>
public class RunRecord
{
    public string RunId         { get; set; } = string.Empty;
    public string TicketId      { get; set; } = string.Empty;
    public string Title         { get; set; } = string.Empty;
    public string Source        { get; set; } = "manual";
    public string? SnowNumber   { get; set; }
    public string? SnowPriority { get; set; }
    public string? SnowCategory { get; set; }
    public string Status        { get; set; } = nameof(PipelineRunStatus.Running);
    public int?   StoryPoints   { get; set; }
    public string? JiraIssueUrl { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Full serialised TicketState JSON — used by the state inspector.</summary>
    public string TicketStateJson { get; set; } = "{}";

    public DateTimeOffset StartedAt   { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public List<StepSnapshotRecord> Snapshots { get; set; } = [];
}
