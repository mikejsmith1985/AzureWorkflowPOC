// Manifest records written to disk after each preflight run (Bootstrap or Adaptive).
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// Base record for both preflight modes. Written to
/// <c>&lt;SpecsRoot&gt;/&lt;feature-dir&gt;/.ado-bootstrap-manifest.json</c>
/// after every successful preflight run.
/// </summary>
public abstract record PreflightManifestBase
{
    /// <summary>Which execution path was taken — Bootstrap or Adaptive.</summary>
    public abstract PreflightMode Mode { get; init; }

    /// <summary>UTC time the preflight completed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>ADO organization URL that was probed (e.g. "https://dev.azure.com/myorg").</summary>
    public required string OrgUrl { get; init; }

    /// <summary>ADO project name that was targeted.</summary>
    public required string Project { get; init; }

    /// <summary>The ADO inherited process type detected for the project.</summary>
    public required AdoProcessType ProcessType { get; init; }
}

/// <summary>
/// Written when the service had admin rights and created (or verified) all custom fields.
/// Invariant: <c>FieldsCreated + FieldsExisting + FieldsFailed</c> covers the full configured field set.
/// </summary>
public sealed record BootstrapManifest : PreflightManifestBase
{
    /// <inheritdoc/>
    public override PreflightMode Mode { get => PreflightMode.Bootstrap; init { } }

    /// <summary>Reference names of fields that were newly created during this run.</summary>
    public required IReadOnlyList<string> FieldsCreated { get; init; }

    /// <summary>Reference names of fields that already existed and were left untouched.</summary>
    public required IReadOnlyList<string> FieldsExisting { get; init; }

    /// <summary>Fields that failed after all <c>MaxRetryAttempts</c> retries.</summary>
    public required IReadOnlyList<FieldBootstrapFailure> FieldsFailed { get; init; }

    /// <summary>Always "preferred" — signals that custom fields are in use (not native fallbacks).</summary>
    public string MappingStrategy { get; init; } = "preferred";
}

/// <summary>
/// Written when the service lacked admin rights and built a native-field fallback mapping instead.
/// </summary>
public sealed record AdaptiveManifest : PreflightManifestBase
{
    /// <inheritdoc/>
    public override PreflightMode Mode { get => PreflightMode.Adaptive; init { } }

    /// <summary>
    /// Maps each desired telemetry field reference name to the native ADO field that will
    /// receive its value (e.g. "Custom.AISessionID" → "System.Tags").
    /// </summary>
    public required IReadOnlyDictionary<string, string> Mapping { get; init; }

    /// <summary>
    /// Desired fields for which no suitable native match exists — they are neither mapped
    /// nor written to ADO. Present only when all fallback strategies are exhausted.
    /// </summary>
    public required IReadOnlyList<string> UnmatchedFields { get; init; }

    /// <summary>Fields whose values are captured in logs only and never written to ADO.</summary>
    public required IReadOnlyList<string> LogOnlyFields { get; init; }
}

/// <summary>
/// Records a single field that could not be created after exhausting all retry attempts.
/// </summary>
public sealed record FieldBootstrapFailure
{
    /// <summary>The ADO reference name of the field that failed (e.g. "Custom.AIOutputTokens").</summary>
    public required string ReferenceName { get; init; }

    /// <summary>The last error message received after all retries were exhausted.</summary>
    public required string Error { get; init; }

    /// <summary>Number of attempts made — always 3 (matches MaxRetryAttempts).</summary>
    public required int AttemptsExhausted { get; init; }
}
