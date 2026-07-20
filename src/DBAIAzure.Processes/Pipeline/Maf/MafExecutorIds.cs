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
    /// <summary>Migration entry that forwards a paused ticket straight to the clarification gate (spec-019 T033).</summary>
    public const string IntakeResumeSeed = "intake-resume-seed";
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

    // ── DoR Validation Workflow (MafDorWorkflowFactory — spec-021) ──────────────────────
    /// <summary>Reads the ticket and loads the DoR document into the review payload.</summary>
    public const string DorHydrate = "dor-hydrate";
    /// <summary>AI structured DoR review (and reply re-evaluation on the loop back).</summary>
    public const string DorReview = "dor-review";
    /// <summary>Transitions a passing ticket to the ready status.</summary>
    public const string DorPass = "dor-pass";
    /// <summary>Posts the gap message and starts the primary SLA clock.</summary>
    public const string DorOutreach = "dor-outreach";
    /// <summary>The human-in-the-loop conversation gate (a RequestPort).</summary>
    public const string DorHitl = "dor-hitl";
    /// <summary>Pass-through that routes the resumed state (reply / escalate / manual-exit) off the gate.</summary>
    public const string DorDispatch = "dor-dispatch";
    /// <summary>Evaluates a human reply against the outstanding DoR gaps.</summary>
    public const string DorReplyEval = "dor-reply-eval";
    /// <summary>Second-tier escalation outreach with its own SLA/iteration budget.</summary>
    public const string DorEscalate = "dor-escalate";
    /// <summary>Applies the whitelisted field updates and transitions the resolved ticket.</summary>
    public const string DorUpdate = "dor-update";
    /// <summary>Tags the ticket for manual intervention without transitioning it.</summary>
    public const string DorManualExit = "dor-manual-exit";
    /// <summary>Terminal audit record for the DoR workflow instance.</summary>
    public const string DorAudit = "dor-audit";
}
