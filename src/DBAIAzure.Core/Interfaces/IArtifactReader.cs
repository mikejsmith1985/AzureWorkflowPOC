// Seam for reading a feature's specs/NNN-feature-name/ artifact files from disk.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Reads the artifact files for a feature directory so the validation step can summarise them.
/// Isolated behind an interface so the SK step never touches the file system directly and can
/// be unit-tested with a fake that returns canned artifacts.
/// </summary>
public interface IArtifactReader
{
    /// <summary>
    /// Resolves the feature directory under the configured specs root and returns its artifact
    /// files (bounded by the configured per-file byte limit and maximum file count). Returns an
    /// empty list when the directory does not exist or contains no readable artifacts, so the
    /// caller can record a "missing artifacts" failure rather than fabricate content (FR-003).
    /// </summary>
    Task<IReadOnlyList<PhaseArtifact>> ReadArtifactsAsync(
        string featureDirectory, CancellationToken cancellationToken = default);
}
