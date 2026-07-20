// Loads the current DoR document through a source-type seam (spec-021 D6): inline / url now, confluence /
// sharepoint deferred behind the same interface.
using DBAIAzure.Core.Models.DorWorkflow;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Resolves and returns the current DoR document for the active configuration, honoring the configured cache
/// window. Best-effort on transient failure: returns the cached copy with a logged warning; throws
/// <see cref="DorDocumentUnavailableException"/> only when no cache exists (so the workflow can degrade to a
/// manual exit rather than review against an empty DoR).
/// </summary>
public interface IDorDocumentSource
{
    /// <summary>Loads the current DoR document (from cache when fresh).</summary>
    Task<DorDocument> LoadAsync(CancellationToken ct = default);
}
