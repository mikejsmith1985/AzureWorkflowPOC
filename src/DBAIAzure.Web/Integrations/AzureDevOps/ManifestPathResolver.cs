// Resolves the path where the ADO bootstrap manifest should be written (spec-009, T026).
using Microsoft.Extensions.Configuration;

namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Determines the file system path for the ADO bootstrap manifest JSON.
/// Reads <c>SpecKit:SpecsRoot</c> from configuration, then reads <c>.specify/feature.json</c>
/// inside that root to find the active feature directory. Falls back to the specs root itself
/// when <c>feature.json</c> is absent.
/// Virtual to allow substitution in unit tests without file-system access.
/// </summary>
public class ManifestPathResolver
{
    private const string ManifestFileName = ".ado-bootstrap-manifest.json";
    private const string FeatureJsonRelativePath = ".specify/feature.json";
    private const string DefaultSpecsRoot = "specs";

    private readonly IConfiguration? _configuration;
    private readonly ILogger<ManifestPathResolver>? _logger;

    public ManifestPathResolver(IConfiguration configuration, ILogger<ManifestPathResolver> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Returns the absolute path where the manifest should be written.
    /// Creates the target directory if it does not exist.
    /// </summary>
    public virtual string Resolve()
    {
        var specsRoot = _configuration?["SpecKit:SpecsRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, DefaultSpecsRoot);

        var featureJsonPath = Path.Combine(specsRoot, FeatureJsonRelativePath);
        string targetDir;

        if (File.Exists(featureJsonPath))
        {
            try
            {
                var json = File.ReadAllText(featureJsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("feature_directory", out var featureProp)
                    && featureProp.GetString() is { Length: > 0 } featureDir)
                {
                    targetDir = Path.Combine(specsRoot, featureDir);
                }
                else
                {
                    targetDir = specsRoot;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not read feature.json — writing manifest to specs root.");
                targetDir = specsRoot;
            }
        }
        else
        {
            targetDir = specsRoot;
        }

        Directory.CreateDirectory(targetDir);
        return Path.Combine(targetDir, ManifestFileName);
    }
}
