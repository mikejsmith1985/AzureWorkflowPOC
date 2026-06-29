// Maps tracker-neutral logical field names to Azure DevOps native reference names (spec-018).
namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Resolves a logical field name (e.g. <c>AIRuntimeCostUSD</c>) to its Azure DevOps reference name
/// (<c>Custom.AIRuntimeCostUSD</c>) — the prefix ADO uses for organisation custom fields. Inputs that are
/// already prefixed (or are system refs like <c>System.Tags</c>) are passed through unchanged, so the live
/// ADO field names are unaffected.
/// </summary>
public sealed class AdoFieldReferenceResolver
{
    private const string CustomPrefix = "Custom.";

    public string ToNativeReference(string logicalField)
    {
        if (string.IsNullOrWhiteSpace(logicalField))
            return logicalField;

        // Already a fully-qualified reference (Custom.* or System.*/Microsoft.*) — leave as-is.
        if (logicalField.Contains('.'))
            return logicalField;

        return CustomPrefix + logicalField;
    }
}
