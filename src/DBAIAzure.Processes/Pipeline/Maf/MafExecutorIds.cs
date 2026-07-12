// Stable executor identifiers for the MAF workflow graphs (spec-019 US1). These IDs are the observable
// contract a parity test asserts against (ExecutorInvokedEvent.ExecutorId), so they are named here once
// and shared by the builders and the tests — never duplicated as magic strings.
namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// The canonical <see cref="Microsoft.Agents.AI.Workflows.Executor"/> ids for the three migrated
/// pipelines. Each id maps 1:1 to a retired Semantic Kernel step, so the migrated workflow reports the
/// same step sequence the SK pipeline did (parity — FR-015 / SC-001). Referenced by both the workflow
/// builders and the parity tests so the sequence assertion has a single source of truth.
/// </summary>
public static class MafExecutorIds
{
    // ── Intake pipeline (IntakePipelineBuilder → MafIntakeWorkflowFactory) ──────────────
    /// <summary>Normalises the incoming ticket (was <c>IntakeStep</c>).</summary>
    public const string Intake = "intake";
    /// <summary>Decides the ready / not-ready path (was <c>ValidationStep</c>).</summary>
    public const string Validation = "validation";
    /// <summary>Generates clarifying questions on the not-ready path (was <c>GapAnalysisStep</c>).</summary>
    public const string GapAnalysis = "gap-analysis";
    /// <summary>The human-in-the-loop clarification gate (was <c>HitlPauseStep</c> → now a RequestPort).</summary>
    public const string IntakeHitl = "intake-hitl";
    /// <summary>Estimates story points on the ready path (was <c>EstimationStep</c>).</summary>
    public const string Estimation = "estimation";
    /// <summary>Terminal step that creates the work item (was <c>ActionStep</c>).</summary>
    public const string Action = "action";

    // ── Phase-handler pipeline (PhaseHandlerPipelineBuilder → MafPhaseHandlerWorkflowFactory) ──
    /// <summary>Reads the phase artifacts (was <c>ReadArtifactsStep</c>).</summary>
    public const string ReadArtifacts = "read-artifacts";
    /// <summary>Validates the phase signal (was <c>PhaseValidationStep</c>).</summary>
    public const string PhaseValidation = "phase-validation";
    /// <summary>The reviewer approval gate (was <c>ApprovalPauseStep</c> → now a RequestPort).</summary>
    public const string ApprovalHitl = "approval-hitl";
    /// <summary>Terminal step that creates the phase work item (was <c>CreateWorkItemStep</c>).</summary>
    public const string CreateWorkItem = "create-work-item";
}
