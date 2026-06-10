using DBAIAzure.Core.Models;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>A single step-level progress event recorded during a pipeline run.</summary>
public record PipelineEvent(
    string StepName,
    string Message,
    ReportLevel Level,
    DateTimeOffset Timestamp);

public enum PipelineRunStatus
{
    Running,
    AwaitingHuman,
    Complete,
    Blocked,
    Failed,
}
