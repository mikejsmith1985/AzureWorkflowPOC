// Bound configuration for the Spec Kit phase handler (artifact bounds, specs root, decision card URL).
namespace DBAIAzure.Web.Integrations.SpecKit;

/// <summary>
/// Strongly-typed Spec Kit configuration bound from the <c>SpecKit</c> section. Centralises the
/// artifact read bounds (which protect the validation LLM call) and the outbound decision-card URL.
/// </summary>
public sealed class SpecKitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SpecKit";

    /// <summary>Repo-relative root under which feature directories live (default <c>specs</c>).</summary>
    public string SpecsRoot { get; set; } = "specs";

    /// <summary>Maximum bytes read per artifact file (protects the LLM call).</summary>
    public int MaxArtifactBytes { get; set; } = 65536;

    /// <summary>Maximum number of artifact files read per phase.</summary>
    public int MaxArtifactFiles { get; set; } = 12;

    /// <summary>Outbound decision-card endpoint; blank disables the push.</summary>
    public string DecisionCardUrl { get; set; } = string.Empty;
}
