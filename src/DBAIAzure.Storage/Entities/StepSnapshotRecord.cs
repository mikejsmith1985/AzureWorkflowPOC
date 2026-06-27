namespace DBAIAzure.Storage.Entities;

/// <summary>
/// Captures TicketState before and after each LLM step — powers the state inspector
/// and time-travel replay in the admin console.
/// </summary>
public class StepSnapshotRecord
{
    public int    Id          { get; set; }
    public string RunId       { get; set; } = string.Empty;
    public string StepName    { get; set; } = string.Empty;
    public string InputJson   { get; set; } = "{}";
    public string OutputJson  { get; set; } = "{}";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
