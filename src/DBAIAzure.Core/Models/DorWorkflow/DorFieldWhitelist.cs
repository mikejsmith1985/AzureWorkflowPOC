// Enforces the AI-editable field whitelist (spec-021 FR-021 / SC-006). Programmatic, not prompt-trusted: no
// matter what the model proposes, only fields on the configured whitelist can ever be written.
namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// Filters an AI-proposed field-update map down to the configured whitelist. This is the single enforcement
/// point — the model is <em>also</em> told the whitelist as guidance, but a non-whitelisted key returned by the
/// model is dropped here regardless (never left to the model to self-limit).
/// </summary>
public static class DorFieldWhitelist
{
    /// <summary>Returns only the proposed entries whose keys appear in <paramref name="editableFields"/> (case-insensitive).</summary>
    public static IReadOnlyDictionary<string, string> Filter(
        IReadOnlyDictionary<string, string> proposed, IReadOnlyCollection<string> editableFields)
    {
        var allowed = new HashSet<string>(editableFields, StringComparer.OrdinalIgnoreCase);
        return proposed
            .Where(entry => allowed.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }
}
