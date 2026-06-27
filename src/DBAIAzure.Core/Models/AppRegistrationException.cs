// Raised when a monitored-app registration is rejected by validation (feature 013).
namespace DBAIAzure.Core.Models;

/// <summary>
/// Thrown when registering a <see cref="MonitoredApp"/> is rejected — a duplicate name for the
/// owner, a non-existent/inaccessible repo path, or a missing run command (FR-002). Callers surface
/// <see cref="Exception.Message"/> as an inline validation error rather than creating an unusable app.
/// </summary>
public sealed class AppRegistrationException : Exception
{
    /// <summary>Creates the exception with a user-facing validation message.</summary>
    public AppRegistrationException(string message) : base(message) { }
}
