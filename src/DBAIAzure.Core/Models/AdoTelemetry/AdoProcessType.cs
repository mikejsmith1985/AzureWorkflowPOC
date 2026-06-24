// Represents the ADO inherited process type detected for the target project.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// Identifies which ADO inherited process template governs the target project.
/// Bootstrap field attachment uses process-specific work item type names
/// (e.g. "User Story" vs "Product Backlog Item") based on this value.
/// </summary>
public enum AdoProcessType
{
    /// <summary>ADO Agile process — story-level WIT is "User Story".</summary>
    Agile,

    /// <summary>ADO Scrum process — story-level WIT is "Product Backlog Item".</summary>
    Scrum,

    /// <summary>
    /// Any other process type (CMMI, hosted XML, custom).
    /// Preflight halts with a clear error when this value is detected.
    /// </summary>
    Unsupported,
}
