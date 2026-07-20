// Implements the AI DoR-review seam (spec-021) over IStructuredCompletionService: interpolates the review
// prompt with the DoR document + ticket fields and forces a schema-bound verdict.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Produces the DoR verdict by interpolating the (configured or default) review template with the DoR document
/// and the ticket fields, then asking <see cref="IStructuredCompletionService"/> for a schema-bound
/// <see cref="DorReviewResult"/>. Reused by both the initial review and the reply re-evaluation loop.
/// </summary>
public sealed class DorReviewService : IDorReviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStructuredCompletionService _completion;

    public DorReviewService(IStructuredCompletionService completion) => _completion = completion;

    /// <inheritdoc/>
    public async Task<DorReviewResult> ReviewAsync(
        IReadOnlyDictionary<string, string?> ticketFields, string dorDocument, DorAiConfig ai, CancellationToken ct = default)
    {
        var template = string.IsNullOrWhiteSpace(ai.ReviewPromptTemplate)
            ? DorPrompts.DefaultReviewTemplate
            : ai.ReviewPromptTemplate;

        var systemPrompt = DorPrompts.Interpolate(template, new Dictionary<string, string>
        {
            ["dor_document"] = dorDocument,
            ["ticket_fields"] = JsonSerializer.Serialize(ticketFields, JsonOptions),
        });

        return await _completion.GetStructuredAsync<DorReviewResult>(
            systemPrompt,
            "Evaluate the ticket against the Definition of Ready and return the structured verdict.",
            "dor_review",
            "Return the structured DoR review verdict.",
            DorSchemas.ReviewSchema,
            ct);
    }
}
