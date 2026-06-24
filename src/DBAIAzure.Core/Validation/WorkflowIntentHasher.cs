// Computes a stable fingerprint of a node's plain-language intent so realized config can be detected
// as out-of-date when the user later changes the intent.

using System.Security.Cryptography;
using System.Text;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Validation;

/// <summary>
/// Produces a deterministic hash of a node's "intent" — its label, goal, type, and the shape of the
/// edges connected to it. Realization records this hash on acceptance; the readiness service recomputes
/// it and, on a mismatch, marks the node out-of-date (R6, FR-13.5). Content-based (not timestamp-based)
/// so unrelated re-saves never trigger a false out-of-date signal.
/// </summary>
public static class WorkflowIntentHasher
{
    // A distinctive multi-character delimiter, very unlikely to occur inside a label or goal, used to
    // separate the hashed fields so concatenation cannot cause accidental collisions.
    private const string FieldSeparator = "<|node-intent|>";

    /// <summary>Computes the intent hash for <paramref name="node"/> within <paramref name="workflow"/>.</summary>
    public static string ComputeIntentHash(WorkflowNode node, WorkflowDefinition workflow)
    {
        // A deterministic signature of every edge touching this node — rewiring changes the hash.
        var edgeSignature = workflow.Edges
            .Where(edge => edge.SourceNodeId == node.Id || edge.TargetNodeId == node.Id)
            .Select(edge => $"{edge.SourceNodeId}:{edge.SourcePortId}->{edge.TargetNodeId}:{edge.TargetPortId}")
            .OrderBy(signature => signature, StringComparer.Ordinal);

        var parts = new[] { node.Label, node.GoalPrompt ?? string.Empty, node.NodeType.ToString() }
            .Concat(edgeSignature);

        var payload = string.Join(FieldSeparator, parts);
        var hash    = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
