// Defines the Spec Kit phases the handler understands and maps each to its Azure DevOps work item type.
namespace DBAIAzure.Core.Models;

/// <summary>
/// The Spec-Driven Development phase named by an inbound signal. The phase drives which
/// kind of tracking work item is produced, so it is the single source of truth for the mapping.
/// </summary>
public enum SpecKitPhase
{
    /// <summary>The Specify phase — produces an Epic that summarises the whole feature.</summary>
    Specify,

    /// <summary>The Plan phase — produces one Task per planned unit of work, under the Epic.</summary>
    Plan,

    /// <summary>The Implement phase — produces a Bug/completion record under the Epic.</summary>
    Implement,

    /// <summary>Any phase value outside the supported set — recorded but never written to the board.</summary>
    Unsupported,
}

/// <summary>
/// Central rule set translating a <see cref="SpecKitPhase"/> into the Azure DevOps work item
/// type string. Keeping the mapping in one place means a different process template can be
/// accommodated without touching the pipeline steps.
/// </summary>
public static class PhaseWorkItemMap
{
    /// <summary>Azure DevOps work item type name for the Specify phase (Agile process).</summary>
    public const string EpicType = "Epic";

    /// <summary>Azure DevOps work item type name for the Plan phase (Agile process).</summary>
    public const string TaskType = "Task";

    /// <summary>Azure DevOps work item type name for the Implement phase (Agile process).</summary>
    public const string BugType = "Bug";

    /// <summary>
    /// Returns the Azure DevOps work item type for the given phase, or <c>null</c> when the
    /// phase is unsupported (the handler records it and creates nothing — FR-014).
    /// </summary>
    public static string? ToWorkItemType(SpecKitPhase phase) => phase switch
    {
        SpecKitPhase.Specify   => EpicType,
        SpecKitPhase.Plan      => TaskType,
        SpecKitPhase.Implement => BugType,
        _                      => null,
    };

    /// <summary>
    /// Parses a free-form phase string from the inbound signal (case-insensitive). Unknown
    /// values map to <see cref="SpecKitPhase.Unsupported"/> so the caller can record them safely.
    /// </summary>
    public static SpecKitPhase Parse(string? rawPhase) => (rawPhase ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "specify"   => SpecKitPhase.Specify,
        "plan"      => SpecKitPhase.Plan,
        "implement" => SpecKitPhase.Implement,
        _           => SpecKitPhase.Unsupported,
    };
}
