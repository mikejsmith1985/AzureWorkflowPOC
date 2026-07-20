// The AI conversation seam (spec-021): interprets a human reply against the outstanding DoR gaps. Extracted from
// the executor so the reply-evaluation logic is unit-testable without MAF plumbing.
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Evaluates a human reply against the outstanding DoR gaps and returns a structured
/// <see cref="ReplyEvaluation"/> (resolved? remaining gaps, implied field updates, a message to post back).
/// The field updates it proposes are advisory — the whitelist is enforced separately in code.
/// </summary>
public interface IDorConversationService
{
    /// <summary>Interprets <paramref name="humanReply"/> against <paramref name="outstandingGaps"/> for the given iteration.</summary>
    Task<ReplyEvaluation> EvaluateReplyAsync(
        IReadOnlyList<string> outstandingGaps,
        string humanReply,
        int iteration,
        DorAiConfig ai,
        CancellationToken ct = default);
}
