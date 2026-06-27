// Configuration record for the Definition of Ready rule set.

namespace DBAIAzure.Core.Configuration;

/// <summary>
/// Binds to the <c>"DorRules"</c> configuration section (appsettings.json or environment).
/// Administrators change this file (or an environment override) to disable specific rules
/// without redeploying the application binary (FR-24.3).
/// </summary>
public sealed record DorRuleSettings
{
    /// <summary>Configuration section key used with IOptions&lt;DorRuleSettings&gt;.</summary>
    public const string SectionName = "DorRules";

    /// <summary>
    /// Rule names that should be skipped during validation.
    /// Values must match <c>IWorkflowReadinessRule.RuleName</c> exactly (case-sensitive).
    /// An empty array means all rules are active.
    /// </summary>
    public string[] DisabledRuleNames { get; init; } = Array.Empty<string>();
}
