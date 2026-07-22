// Canonical defaults for the Definition-of-Ready document: the reference name the AI review step's
// document is stored under, and the sample checklist a new workflow starts from.

namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// Shared defaults for the Definition-of-Ready document so the Workflow Builder's AI review node and the
/// configuration card use a single source of truth. <see cref="ReferenceName"/> is the stable label the DoR
/// document is attached under as a Document reference on the AI review node — the node-config assembler will
/// locate the active DoR document by this name — and <see cref="SampleMarkdown"/> is the editable starter
/// checklist an operator adapts to their own Definition of Ready.
/// </summary>
public static class DorDocumentDefaults
{
    /// <summary>The reference name the DoR document is attached under on the AI review node.</summary>
    public const string ReferenceName = "Definition of Ready";

    /// <summary>A short, editable Definition-of-Ready checklist used as the starter document.</summary>
    public const string SampleMarkdown = """
        # Definition of Ready

        A ticket is Ready to Work when all of the following are true:

        1. **Summary** — clearly states the desired outcome in one sentence.
        2. **Description** — explains the business context and the "why".
        3. **Acceptance Criteria** — at least one testable, unambiguous criterion.
        4. **Estimate** — a story-point or effort estimate is present.
        5. **Dependencies** — any blocking work or external dependency is named.
        """;
}
