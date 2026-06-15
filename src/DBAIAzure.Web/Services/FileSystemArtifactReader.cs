// IArtifactReader implementation that reads a feature's markdown artifacts from the repo specs/ tree.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Integrations.SpecKit;
using Microsoft.Extensions.Options;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Reads phase artifact files from disk under the configured specs root, recursively (including
/// subdirectories such as contracts/ and checklists/) so the validation sees the whole feature.
/// Reads are bounded by the configured per-file byte limit and maximum file count so a large or
/// malicious directory cannot blow up the validation LLM call. Returns an empty list when the
/// directory is missing or has no readable artifacts, so the caller records a "missing artifacts"
/// failure rather than inventing content.
/// </summary>
public sealed class FileSystemArtifactReader : IArtifactReader
{
    /// <summary>Artifact file extensions considered readable phase content.</summary>
    private static readonly string[] ArtifactExtensions = [".md", ".json"];

    private readonly SpecKitOptions _options;
    private readonly string _contentRoot;
    private readonly ILogger<FileSystemArtifactReader> _logger;

    public FileSystemArtifactReader(
        IOptions<SpecKitOptions> options,
        IWebHostEnvironment environment,
        ILogger<FileSystemArtifactReader> logger)
    {
        _options = options.Value;
        _contentRoot = environment.ContentRootPath;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PhaseArtifact>> ReadArtifactsAsync(
        string featureDirectory, CancellationToken cancellationToken = default)
    {
        var resolvedDirectory = ResolveDirectory(featureDirectory);
        if (resolvedDirectory is null || !Directory.Exists(resolvedDirectory))
        {
            _logger.LogWarning("Artifact directory not found for '{FeatureDirectory}'", featureDirectory);
            return [];
        }

        // Recurse into subdirectories (e.g. contracts/, checklists/) so validation sees the whole
        // feature, not just top-level files. The file-count cap still bounds the total read.
        var files = Directory
            .EnumerateFiles(resolvedDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => ArtifactExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .OrderBy(path => Path.GetRelativePath(resolvedDirectory, path), StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaxArtifactFiles)
            .ToList();

        var artifacts = new List<PhaseArtifact>(files.Count);
        foreach (var path in files)
        {
            var content = await ReadBoundedAsync(path, cancellationToken);
            // Use the path relative to the feature dir (forward-slashed) so subdirectory files are
            // distinguishable (e.g. "contracts/http-endpoints.md") and never collide on bare names.
            var relativeName = Path.GetRelativePath(resolvedDirectory, path).Replace('\\', '/');
            artifacts.Add(new PhaseArtifact { FileName = relativeName, Content = content });
        }

        _logger.LogInformation(
            "Read {Count} artifact(s) for '{FeatureDirectory}': {Names}",
            artifacts.Count, featureDirectory, string.Join(", ", artifacts.Select(artifact => artifact.FileName)));

        return artifacts;
    }

    /// <summary>
    /// Resolves the feature directory under the specs root, guarding against path traversal so a
    /// signal cannot read outside the configured tree (Article IX safety).
    /// </summary>
    private string? ResolveDirectory(string featureDirectory)
    {
        if (string.IsNullOrWhiteSpace(featureDirectory)) return null;

        var specsRootFull = Path.GetFullPath(Path.Combine(_contentRoot, _options.SpecsRoot));

        // Accept either a path already rooted at specs/ or a bare feature slug.
        var candidate = featureDirectory.Replace('\\', '/').TrimStart('/');
        var combined = candidate.StartsWith(_options.SpecsRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_contentRoot, candidate)
            : Path.Combine(specsRootFull, candidate);

        var fullPath = Path.GetFullPath(combined);

        // Reject anything that escapes the specs root.
        return fullPath.StartsWith(specsRootFull, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    /// <summary>Reads at most <c>MaxArtifactBytes</c> of UTF-8 text from a file.</summary>
    private async Task<string> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var length = Math.Min(bytes.Length, _options.MaxArtifactBytes);
        return System.Text.Encoding.UTF8.GetString(bytes, 0, length);
    }
}
