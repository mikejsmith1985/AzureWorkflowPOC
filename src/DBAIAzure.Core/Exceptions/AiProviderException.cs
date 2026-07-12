// Raised when the configured AI provider cannot be resolved or initialised — surfaced with the
// provider named so misconfiguration is actionable and never silently swallowed (spec-019 FR-009d).
namespace DBAIAzure.Core.Exceptions;

/// <summary>
/// Thrown when the active AI provider cannot be resolved or built (spec-019 "bring your own AI",
/// FR-009d). The message names the offending provider so an operator can fix the configuration; the
/// system deliberately does NOT fall back to a different provider.
/// </summary>
public sealed class AiProviderException : Exception
{
    /// <summary>The provider id that failed to resolve or initialise.</summary>
    public string ProviderId { get; }

    /// <summary>Creates the exception for a named provider with an actionable message.</summary>
    public AiProviderException(string providerId, string message, Exception? innerException = null)
        : base(message, innerException) => ProviderId = providerId;
}
