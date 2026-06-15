// Typed event-name constants for the phase-handler SK process graph (mirrors Events.cs).
namespace DBAIAzure.Processes;

/// <summary>
/// Event names wiring the phase-handler process graph. Centralising them as constants guards
/// against silent renames that would break step wiring (mirrors the ticket pipeline's Events).
/// </summary>
public static class PhaseHandlerEvents
{
    /// <summary>Entry: a phase-complete signal was accepted and a run started.</summary>
    public const string PhaseSignalReceived = "PhaseSignalReceived";

    /// <summary>ReadArtifactsStep output: artifacts were read successfully.</summary>
    public const string ArtifactsRead = "ArtifactsRead";

    /// <summary>PhaseValidationStep output: structured summary + gaps produced.</summary>
    public const string Validated = "Validated";

    /// <summary>ApprovalPauseStep output: external event that suspends the process for review.</summary>
    public const string AwaitApproval = "AwaitApproval";

    /// <summary>Resume input: the reviewer's decision was delivered back to the process.</summary>
    public const string ApprovalDecided = "ApprovalDecided";

    /// <summary>CreateWorkItemStep output: the work item was created/upserted on the board.</summary>
    public const string WorkItemWritten = "WorkItemWritten";

    /// <summary>The signalled phase is outside the supported set; no work item is created (FR-014).</summary>
    public const string Unsupported = "Unsupported";

    /// <summary>A read, validate, or board-write error was recorded (FR-015).</summary>
    public const string Failed = "Failed";
}
