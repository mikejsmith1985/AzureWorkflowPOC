using DBAIAzure.Core.Models;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>A single step-level log event recorded during a pipeline run.</summary>
public record PipelineEvent(
    string StepName,
    string Message,
    ReportLevel Level,
    DateTimeOffset Timestamp);

/// <summary>
/// A single LLM streaming token emitted during a step's generation phase.
/// Accumulated in PipelineRun.TokenStream and rendered live in the UI.
/// </summary>
public record StreamToken(
    string StepName,
    string Token,
    DateTimeOffset Timestamp);

/// <summary>
/// In-memory TicketState snapshot captured before and after each LLM step.
/// Stored in PipelineRun for live display and persisted to SQLite for time-travel replay.
/// </summary>
public record StepStateSnapshot(
    string StepName,
    string InputJson,
    string OutputJson,
    DateTimeOffset Timestamp);

public enum PipelineRunStatus
{
    Running,
    AwaitingHuman,
    Complete,
    Blocked,
    Failed,
}
