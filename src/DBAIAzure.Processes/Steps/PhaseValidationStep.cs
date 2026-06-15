// SK step: produces the schema-bound validation summary + flagged gaps via a forced tool-use LLM call.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using System.Text;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Summarises the phase artifacts and flags gaps for the reviewer. Uses
/// <see cref="IStructuredCompletionService"/> (Anthropic forced tool use) so the output is bound
/// straight to <see cref="PhaseValidationResult"/> — no free-text parsing (Article VII / FR-004).
/// Emits <see cref="PhaseHandlerEvents.Validated"/> on success, <see cref="PhaseHandlerEvents.Failed"/> otherwise.
/// </summary>
public sealed class PhaseValidationStep : KernelProcessStep
{
    /// <summary>The forced tool name; matches contracts/validation-tool-schema.json.</summary>
    public const string ToolName = "report_phase_validation";

    /// <summary>Human-readable purpose of the tool, sent to the model.</summary>
    public const string ToolDescription =
        "Report a plain-language summary of the spec-kit phase artifacts and any flagged gaps, " +
        "risks, or omissions a reviewer should see before approving a tracking work item.";

    /// <summary>The forced tool's input schema (kept in sync with contracts/validation-tool-schema.json).</summary>
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

    /// <summary>Runs the structured validation call and routes the next event.</summary>
    [KernelFunction]
    public async Task ValidateAsync(KernelProcessStepContext ctx, PhaseHandlerState state, Kernel kernel)
    {
        var structuredService = kernel.GetRequiredService<IStructuredCompletionService>();
        var sink = kernel.Services.GetService<IPhaseProgressSink>();
        var userMessage = BuildUserMessage(state);

        try
        {
            var validation = await structuredService.GetStructuredAsync<PhaseValidationResult>(
                SystemPrompt, userMessage, ToolName, ToolDescription, InputSchemaJson);

            var validated = state with { Validation = validation, Status = PhaseRunStatus.Validated };
            sink?.Report(validated);
            await ctx.EmitEventAsync(new() { Id = PhaseHandlerEvents.Validated, Data = validated });
        }
        catch (Exception ex)
        {
            var failed = state with
            {
                Status = PhaseRunStatus.Failed,
                FailureReason = $"Validation failed: {ex.Message}",
            };
            sink?.Report(failed);
            await ctx.EmitEventAsync(new() { Id = PhaseHandlerEvents.Failed, Data = failed });
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
