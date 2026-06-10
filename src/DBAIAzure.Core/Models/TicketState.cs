namespace DBAIAzure.Core.Models;

public record TicketState
{
    public required string TicketId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    // Populated by ValidationStep when the ticket is not ready
    public IReadOnlyList<string> ClarifyingQuestions { get; init; } = [];

    // Human answer appended by the HITL flow; used in re-validation
    public string? HumanAnswer { get; init; }

    // How many times we have asked for clarification (capped at 3)
    public int ClarificationRound { get; init; }

    // Fibonacci story points assigned by EstimationStep
    public int? StoryPoints { get; init; }

    // Short justification from the LLM anchoring its estimate to a known reference task
    public string? EstimationReasoning { get; init; }

    // Mock Jira issue URL produced by ActionStep
    public string? JiraIssueUrl { get; init; }
}
