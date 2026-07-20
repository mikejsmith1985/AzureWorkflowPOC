// Implements the AI conversation seam (spec-021) over IStructuredCompletionService: interpolates the
// conversation prompt with the outstanding gaps + the human reply and forces a schema-bound evaluation.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Produces a <see cref="ReplyEvaluation"/> by interpolating the (configured or default) conversation template
/// with the outstanding gaps, the human reply, and the iteration count, then asking
/// <see cref="IStructuredCompletionService"/> for a schema-bound result.
/// </summary>
public sealed class DorConversationService : IDorConversationService
{
    private readonly IStructuredCompletionService _completion;

    public DorConversationService(IStructuredCompletionService completion) => _completion = completion;

    /// <inheritdoc/>
    public async Task<ReplyEvaluation> EvaluateReplyAsync(
        IReadOnlyList<string> outstandingGaps, string humanReply, int iteration, DorAiConfig ai, CancellationToken ct = default)
    {
        var template = string.IsNullOrWhiteSpace(ai.ConversationPromptTemplate)
            ? DorPrompts.DefaultConversationTemplate
            : ai.ConversationPromptTemplate;

        var systemPrompt = DorPrompts.Interpolate(template, new Dictionary<string, string>
        {
            ["failed_criteria"] = outstandingGaps.Count > 0 ? "- " + string.Join("\n- ", outstandingGaps) : "(none)",
            ["human_response"] = humanReply,
            ["iteration_count"] = iteration.ToString(),
        });

        return await _completion.GetStructuredAsync<ReplyEvaluation>(
            systemPrompt,
            "Interpret the human reply against the outstanding gaps and return the evaluation.",
            "dor_reply_eval",
            "Return the structured reply evaluation.",
            DorSchemas.ReplyEvaluationSchema,
            ct);
    }
}
