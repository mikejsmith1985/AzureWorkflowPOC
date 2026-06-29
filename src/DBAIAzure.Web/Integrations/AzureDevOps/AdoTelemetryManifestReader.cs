// Reads the ADO preflight manifest and resolves which ADO field each telemetry field should write to.
using System.Text.Json;
using DBAIAzure.Core.Models.AdoTelemetry;

namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// The field targets resolved from a preflight manifest: for each desired telemetry field reference
/// name, the ADO field that actually receives its value. In Bootstrap mode a field maps to itself
/// (the created/existing custom field); in Adaptive mode it maps to a native fallback field. Fields
/// that were not created, are log-only, or are unmatched are simply absent from the dictionary.
/// </summary>
public sealed record ResolvedTelemetryTargets(
    PreflightMode Mode,
    IReadOnlyDictionary<string, string> TargetByFieldRef);

/// <summary>Reads the ADO bootstrap manifest written by the preflight and resolves its field targets.</summary>
public interface IAdoTelemetryManifestReader
{
    /// <summary>
    /// Returns the resolved field targets from the manifest on disk, or null when no manifest exists
    /// (the preflight has not run) or it cannot be read.
    /// </summary>
    Task<ResolvedTelemetryTargets?> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the manifest JSON the preflight wrote (located via <see cref="ManifestPathResolver"/>) and
/// turns it into a flat field-reference → target-reference map the write-back service consults.
/// </summary>
public sealed class AdoTelemetryManifestReader : IAdoTelemetryManifestReader
{
    private const string AdaptiveModeValue = "adaptive";

    private readonly ManifestPathResolver _pathResolver;
    private readonly ILogger<AdoTelemetryManifestReader> _logger;

    public AdoTelemetryManifestReader(ManifestPathResolver pathResolver, ILogger<AdoTelemetryManifestReader> logger)
    {
        _pathResolver = pathResolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ResolvedTelemetryTargets?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var manifestPath = _pathResolver.Resolve();
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var isAdaptive = root.TryGetProperty("mode", out var modeElement)
                && string.Equals(modeElement.GetString(), AdaptiveModeValue, StringComparison.OrdinalIgnoreCase);

            var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (isAdaptive)
                ReadAdaptiveMapping(root, targets);
            else
                ReadBootstrapFields(root, targets);

            return new ResolvedTelemetryTargets(
                isAdaptive ? PreflightMode.Adaptive : PreflightMode.Bootstrap, targets);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read ADO telemetry manifest at {Path}.", manifestPath);
            return null;
        }
    }

    // Bootstrap manifest: created + existing custom fields each map to themselves; failed ones are omitted.
    private static void ReadBootstrapFields(JsonElement root, Dictionary<string, string> targets)
    {
        AddSelfMappedArray(root, "fieldsCreated", targets);
        AddSelfMappedArray(root, "fieldsExisting", targets);
    }

    // Adaptive manifest: each desired field maps to the native fallback field chosen by the preflight.
    private static void ReadAdaptiveMapping(JsonElement root, Dictionary<string, string> targets)
    {
        if (!root.TryGetProperty("mapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object)
            return;

        foreach (var entry in mapping.EnumerateObject())
            if (entry.Value.GetString() is { Length: > 0 } targetRef)
                targets[entry.Name] = targetRef;
    }

    private static void AddSelfMappedArray(JsonElement root, string propertyName, Dictionary<string, string> targets)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            return;

        foreach (var element in array.EnumerateArray())
            if (element.GetString() is { Length: > 0 } referenceName)
                targets[referenceName] = referenceName;
    }
}
