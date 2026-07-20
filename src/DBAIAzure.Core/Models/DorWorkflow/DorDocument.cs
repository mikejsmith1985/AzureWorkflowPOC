// The Definition-of-Ready document loaded through the source-type seam (spec-021). Its text is the criteria the
// AI evaluates against — the criteria live in the document, not in code (FR-006).
namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// A point-in-time load of the DoR document. <see cref="Text"/> is injected into the review prompt; <see
/// cref="Version"/> (etag / last-modified / config hash) is recorded in the audit so a historical record can
/// reference the exact DoR in effect at review time.
/// </summary>
public sealed record DorDocument(
    string Text,
    string? Version,
    DateTimeOffset LoadedAt,
    string SourceType);

/// <summary>
/// Raised when the DoR document cannot be loaded and no cached copy exists — the workflow must NOT review a
/// ticket against an empty DoR, so it degrades to a manual exit with this reason instead.
/// </summary>
public sealed class DorDocumentUnavailableException : Exception
{
    public DorDocumentUnavailableException(string message) : base(message) { }
    public DorDocumentUnavailableException(string message, Exception inner) : base(message, inner) { }
}
