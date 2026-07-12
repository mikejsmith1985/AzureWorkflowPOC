// MAF executor that validates phase artifacts into a schema-bound result (spec-019 T017) — the GA
// replacement for the SK PhaseValidationStep. Uses IStructuredCompletionService (now atop IChatClient)
// so the output binds straight to PhaseValidationResult; same DoR gate and failure routing (parity).
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Summarises the phase artifacts and flags gaps for the reviewer via structured (schema-bound) output,
/// then forwards the validated state to the approval gate. A missing/invalid cost binding key fails the
/// Definition-of-Ready gate, and any validation error yields a failed terminal state — never a board
/// write. Mirrors the retired <c>PhaseValidationStep</c> (same tool schema and prompts).
/// </summary>
[SendsMessage(typeof(PhaseHandlerState))]
[YieldsOutput(typeof(PhaseHandlerState))]
public sealed class PhaseValidationExecutor : Executor<PhaseHandlerState>
{
    /// <summary>The forced tool name; matches contracts/validation-tool-schema.json.</summary>
    public const string ToolName = "report_phase_validation";

    /// <summary>Human-readable purpose of the tool, sent to the model.</summary>
    public const string ToolDescription =
        "Report a plain-language summary of the spec-kit phase artifacts and any flagged gaps, " +
        "risks, or omissions a reviewer should see before approving a tracking work item.";

    /// <summary>The structured output schema (kept in sync with contracts/validation-tool-schema.json).</summary>
    public const string InputSchemaJson = """
        {
          "type": "object",
          "properties": {
            "summary": {
              "type": "string",
              "description": "One-paragraph plain-language summary of what this phase's artifacts contain."
            },
            "gaps": {
              "type": "array",
              "description": "Flagged gaps, risks, or omissions. Empty array if none were found.",
              "items": {
                "type": "object",
                "properties": {
                  "label": { "type": "string", "description": "Short label for the gap (a few words)." },
                  "description": { "type": "string", "description": "What is missing or risky, and why it matters for the next phase." }
                },
                "required": ["label", "description"],
                "additionalProperties": false
              }
            }
          },
          "required": ["summary", "gaps"],
          "additionalProperties": false
        }
        """;

    private const string SystemPrompt =
        "You are a meticulous spec-driven-development reviewer. Read the provided phase artifacts and " +
        "report a concise plain-language summary plus any gaps, risks, or omissions a reviewer must see " +
        "before approving a tracking work item. Base everything strictly on the artifacts — never invent content.";

    private readonly IStructuredCompletionService _structuredService;
    private readonly IBindingKeyMinter? _bindingKeyMinter;
    private readonly IPhaseProgressSink? _progressSink;

    /// <summary>Creates the executor over the structured-completion service and optional gate/sink dependencies.</summary>
    public PhaseValidationExecutor(
        IStructuredCompletionService structuredService,
        IBindingKeyMinter? bindingKeyMinter = null,
        IPhaseProgressSink? progressSink = null)
        : base(MafExecutorIds.PhaseValidation)
    {
        _structuredService = structuredService;
        _bindingKeyMinter = bindingKeyMinter;
        _progressSink = progressSink;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(PhaseHandlerState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        // DoR gate (spec-017 FR-002): a ticket cannot be ready without a valid cost binding key.
        if (_bindingKeyMinter is not null && !_bindingKeyMinter.IsValid(state.CostBindingKey))
        {
            var blocked = state with
            {
                Status = PhaseRunStatus.Failed,
                FailureReason = "Definition of Ready: missing or invalid cost binding key.",
            };
            _progressSink?.Report(blocked);
            await context.YieldOutputAsync(blocked, cancellationToken);
            return;
        }

        try
        {
            var validation = await _structuredService.GetStructuredAsync<PhaseValidationResult>(
                SystemPrompt, BuildUserMessage(state), ToolName, ToolDescription, InputSchemaJson, cancellationToken);

            var validated = state with { Validation = validation, Status = PhaseRunStatus.Validated };
            _progressSink?.Report(validated);
            await context.SendMessageAsync(validated, cancellationToken);
        }
        catch (Exception exception)
        {
            var failed = state with
            {
                Status = PhaseRunStatus.Failed,
                FailureReason = $"Validation failed: {exception.Message}",
            };
            _progressSink?.Report(failed);
            await context.YieldOutputAsync(failed, cancellationToken);
        }
    }

    /// <summary>Assembles the artifact contents into a single prompt body for the model.</summary>
    private static string BuildUserMessage(PhaseHandlerState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Feature: {state.FeatureKey}");
        builder.AppendLine($"Phase: {state.Phase}");
        builder.AppendLine();
        builder.AppendLine("Artifacts:");
        foreach (var artifact in state.Artifacts)
        {
            builder.AppendLine($"----- {artifact.FileName} -----");
            builder.AppendLine(artifact.Content);
            builder.AppendLine();
        }
        return builder.ToString();
    }
}
