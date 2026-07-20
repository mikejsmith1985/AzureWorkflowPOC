// The AI DoR-review seam (spec-021): evaluates a ticket's fields against the DoR document and returns a
// structured verdict. Extracted from the executor so the review logic is unit-testable without MAF plumbing.
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Evaluates a ticket against the DoR document using the configured AI model and returns a structured
/// <see cref="DorReviewResult"/> (schema-bound — no free-text parsing). The criteria come from the DoR document,
/// not from code (FR-006).
/// </summary>
public interface IDorReviewService
{
    /// <summary>Reviews the ticket's watched fields against the DoR document text.</summary>
    Task<DorReviewResult> ReviewAsync(
        IReadOnlyDictionary<string, string?> ticketFields,
        string dorDocument,
        DorAiConfig ai,
        CancellationToken ct = default);
}
