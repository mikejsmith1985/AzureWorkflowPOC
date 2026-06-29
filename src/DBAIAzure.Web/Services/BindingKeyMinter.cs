// Mints the source-neutral, branch-safe binding key that joins a ticket to its AI-cost records.
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Default <see cref="IBindingKeyMinter"/>: produces <c>BIND-XXXXXXXX</c> keys (uppercase alphanumeric
/// from a GUID) that are safe to use as a git branch segment and an ADO query value. Validation accepts
/// any non-blank, branch-safe string (letters, digits, hyphens) so externally-supplied keys also pass.
/// </summary>
public sealed class BindingKeyMinter : IBindingKeyMinter
{
    private const string Prefix = "BIND-";
    private const int BodyLength = 8;
    private const int MinValidLength = 4;

    /// <inheritdoc/>
    public string Mint() =>
        Prefix + Guid.NewGuid().ToString("N").ToUpperInvariant()[..BodyLength];

    /// <inheritdoc/>
    public bool IsValid(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        // Branch-safe: only letters, digits, and hyphens — rejects whitespace, slashes, etc.
        return candidate.Length >= MinValidLength
            && candidate.All(character => char.IsLetterOrDigit(character) || character == '-');
    }
}
