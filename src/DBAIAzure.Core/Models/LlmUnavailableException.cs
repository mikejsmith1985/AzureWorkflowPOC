// Defines LlmUnavailableException — a typed signal that the LLM backend could not be reached,
// allowing callers to degrade gracefully without crashing the canvas.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Thrown by <c>IWorkflowCodeGenerator</c> and <c>IWorkflowExecutionOrchestrator</c> when the
/// configured LLM endpoint is unreachable or returns a non-recoverable error.
/// Callers are expected to surface a plain-language message to the user; the visual canvas
/// remains fully operational so the user can continue editing without a working LLM connection.
/// </summary>
public sealed class LlmUnavailableException : Exception
{
    /// <summary>
    /// Initialises a new <see cref="LlmUnavailableException"/> with a human-readable description
    /// of why the LLM could not be reached.
    /// </summary>
    /// <param name="message">
    /// A plain-language explanation of the failure, suitable for display in the UI error banner.
    /// </param>
    public LlmUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="LlmUnavailableException"/> that wraps an underlying transport
    /// or protocol error, preserving the full exception chain for diagnostics.
    /// </summary>
    /// <param name="message">
    /// A plain-language explanation of the failure, suitable for display in the UI error banner.
    /// </param>
    /// <param name="innerException">
    /// The original exception that caused the LLM to be considered unavailable (e.g. an
    /// <see cref="HttpRequestException"/> or a vendor SDK timeout).
    /// </param>
    public LlmUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
