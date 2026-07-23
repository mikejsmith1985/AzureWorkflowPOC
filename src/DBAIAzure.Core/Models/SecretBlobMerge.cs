// Merges newly-typed credentials into a connector's existing encrypted secrets blob, so saving one credential
// never silently erases the others stored alongside it.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DBAIAzure.Core.Models;

/// <summary>
/// Combines the secrets an operator just typed with the ones already stored for a connector. A connector row
/// holds every one of its credentials in a single JSON blob, so a form that writes only the field it changed
/// would wipe the rest — this keeps untouched values intact. Pure and side-effect free.
/// </summary>
public static class SecretBlobMerge
{
    /// <summary>
    /// Returns the blob to persist, or null meaning "leave the stored blob exactly as it is" (nothing was typed).
    /// Entries in <paramref name="enteredSecrets"/> whose value is null or blank are treated as untouched and
    /// keep whatever the existing blob holds; entries with a value replace it.
    /// </summary>
    /// <param name="existingSecretsJson">The connector's current decrypted secrets blob, if any.</param>
    /// <param name="enteredSecrets">Secret name → the value typed in the form (blank when left alone).</param>
    public static string? Merge(string? existingSecretsJson, IReadOnlyDictionary<string, string?> enteredSecrets)
    {
        var typed = enteredSecrets
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToList();

        if (typed.Count == 0)
            return null;

        var merged = ParseExisting(existingSecretsJson);
        foreach (var (name, value) in typed)
            merged[name] = value;

        return merged.ToJsonString();
    }

    /// <summary>Parses the stored blob into a mutable object; an absent or malformed blob starts empty.</summary>
    private static JsonObject ParseExisting(string? existingSecretsJson)
    {
        if (string.IsNullOrWhiteSpace(existingSecretsJson))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(existingSecretsJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            // A blob we cannot read is replaced rather than merged — the operator's new values still land.
            return new JsonObject();
        }
    }
}
