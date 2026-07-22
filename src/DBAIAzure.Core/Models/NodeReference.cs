// Represents a typed external reference attached to a single workflow node — a document, URL,
// dashboard, or binary pointer that the node consults while it runs.

namespace DBAIAzure.Core.Models;

/// <summary>
/// The kind of thing a <see cref="NodeReference"/> points at. Kept small and closed so the builder can
/// render the right editor (a document gets a text area; a URL gets a single-line box) and the runtime
/// can resolve each kind consistently.
/// </summary>
public enum NodeReferenceType
{
    /// <summary>Inline or fetched text/markdown document (for example a Definition-of-Ready checklist).</summary>
    Document = 0,

    /// <summary>A web address the node should read from or link to.</summary>
    Url = 1,

    /// <summary>A dashboard the node references for context, held as an address or identifier.</summary>
    Dashboard = 2,

    /// <summary>An opaque binary/artifact pointer, such as a blob URI or attachment identifier.</summary>
    Binary = 3,
}

/// <summary>
/// A single typed reference a workflow node carries in its configuration. A reference is owned by exactly
/// one node — unlike a free-floating canvas node it is never ambiguous which step consults it.
/// <see cref="Name"/> is the human label shown in the builder, and <see cref="Value"/> holds the payload
/// or pointer: the inline document text for <see cref="NodeReferenceType.Document"/>, or the
/// address/identifier for the URL, dashboard, and binary kinds.
/// </summary>
public sealed record NodeReference
{
    /// <summary>The kind of reference, which drives how it is edited in the builder and later resolved.</summary>
    public required NodeReferenceType Type { get; init; }

    /// <summary>Human-readable label for the reference (for example "Definition of Ready").</summary>
    public required string Name { get; init; }

    /// <summary>The reference payload: inline document text, or an address/identifier for the other kinds.</summary>
    public string Value { get; init; } = string.Empty;
}
