// The active work-tracker configuration resolved from the connector store at run time (spec-020).
namespace DBAIAzure.Core.Models;

/// <summary>
/// A point-in-time snapshot of the active Work Tracking System connector, resolved per run from the store by
/// <c>IWorkTrackerConfigResolver</c>. Carries the selected provider plus the raw non-secret JSON and the
/// decrypted secret JSON so each consumer (ADO client, Jira connection factory, tester, adapter provider) can
/// parse only what it needs. The decrypted secret is populated server-side only and MUST NOT reach the UI.
/// </summary>
public sealed record ResolvedWorkTrackerConfig(
    /// <summary>Which provider is active on the connector.</summary>
    WorkTrackerProvider Provider,

    /// <summary>Raw non-secret configuration JSON (the discriminated shape), or null when unconfigured.</summary>
    string? NonSecretJson,

    /// <summary>Decrypted secret JSON (server-side only), or null when no secret is stored.</summary>
    string? DecryptedSecret,

    /// <summary>True when a provider is selected and the connector has been configured.</summary>
    bool IsConfigured)
{
    /// <summary>An unconfigured result — no provider selected and no credentials available.</summary>
    public static ResolvedWorkTrackerConfig Unconfigured { get; } =
        new(WorkTrackerProvider.AzureDevOps, NonSecretJson: null, DecryptedSecret: null, IsConfigured: false);
}
