// Strips secrets from captured container logs before they are persisted/displayed (feature 013, R6).
using System.Text.RegularExpressions;

namespace DBAIAzure.Connectors.Apps;

/// <summary>
/// Removes secret values from captured build/run logs so no secret is ever persisted or displayed
/// (FR-009, Article IX). Redacts known values (e.g. an access token passed for a build) plus common
/// credential patterns (bearer tokens, "key=…", URLs with inline credentials).
/// </summary>
public static partial class ContainerLogRedactor
{
    private const string Mask = "***REDACTED***";

    /// <summary>
    /// Returns <paramref name="logs"/> with <paramref name="knownSecrets"/> and common credential
    /// patterns masked. Null/empty input returns an empty string.
    /// </summary>
    public static string Redact(string? logs, params string?[] knownSecrets)
    {
        if (string.IsNullOrEmpty(logs))
            return string.Empty;

        var result = logs;

        // 1) Exact known secret values (e.g. an access token handed to the build).
        foreach (var secret in knownSecrets)
        {
            if (!string.IsNullOrWhiteSpace(secret) && secret!.Length >= 4)
                result = result.Replace(secret, Mask, StringComparison.Ordinal);
        }

        // 2) Common credential patterns.
        result = BearerPattern().Replace(result, $"Bearer {Mask}");
        result = KeyAssignmentPattern().Replace(result, m => $"{m.Groups[1].Value}{Mask}");
        result = UrlCredentialPattern().Replace(result, $"://{Mask}@");

        return result;
    }

    // "Bearer <token>"
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    // key=..., token=..., password=..., secret=..., apikey=... up to the next whitespace.
    [GeneratedRegex(@"(?i)\b((?:api[_-]?key|token|password|passwd|secret)\s*[=:]\s*)\S+")]
    private static partial Regex KeyAssignmentPattern();

    // scheme://user:pass@host → scheme://***REDACTED***@host
    [GeneratedRegex(@"://[^/\s:@]+:[^/\s:@]+@")]
    private static partial Regex UrlCredentialPattern();
}
