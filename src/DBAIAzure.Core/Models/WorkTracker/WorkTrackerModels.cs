// Tracker-neutral value types used by the work-tracker adapter abstraction (spec-018).
namespace DBAIAzure.Core.Models.WorkTracker;

/// <summary>The logical work item types the pipeline creates; each adapter maps to its native type.</summary>
public enum WorkItemType
{
    Epic,
    UserStory,
    Task,
    Bug,
}

/// <summary>How a tracker provides hierarchical cost rollup.</summary>
public enum RollupKind
{
    /// <summary>The tracker sums the cost fields up the hierarchy natively (e.g. ADO Analytics).</summary>
    Native,

    /// <summary>Rollup needs an add-on the instance may not have (e.g. Jira Advanced Roadmaps).</summary>
    RequiresAddOn,

    /// <summary>No hierarchical rollup available.</summary>
    None,
}

/// <summary>What rollup a tracker offers, plus an operator-facing notice when it is not native (FR-010).</summary>
public sealed record RollupCapability(RollupKind Kind, string? NativeTool = null, string? Notice = null);

/// <summary>One field that could not be made usable during provisioning, with an actionable reason.</summary>
public sealed record FieldProvisioningFailure(string Field, string Reason);

/// <summary>The outcome of provisioning the logical fields on a tracker (idempotent — FR-008).</summary>
public sealed record ProvisioningResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>Tracker-specific provisioning mode (e.g. ADO "Bootstrap"/"Adaptive"; Jira "ContextScreen").</summary>
    public required string Mode { get; init; }

    public IReadOnlyList<string> FieldsReady { get; init; } = [];
    public IReadOnlyList<FieldProvisioningFailure> FieldsFailed { get; init; } = [];
}

/// <summary>
/// Optional routing hint for selecting an adapter. Unused in v1 (single active tracker per instance) —
/// reserved so per-project / per-workflow routing can be added later without changing the core (FR-005).
/// </summary>
public sealed record WorkRoutingContext(string? FeatureKey = null, string? ProjectKey = null);
