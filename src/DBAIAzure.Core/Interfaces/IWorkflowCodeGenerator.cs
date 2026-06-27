// Defines the contract for generating and refining Semantic Kernel process code from workflow definitions.

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Generates and refines Semantic Kernel process code from a <see cref="WorkflowDefinition"/>.
/// Implementations stream tokens to the caller via an <see cref="Action{T}"/> callback so the
/// UI can display a live typing effect without waiting for the full response.
/// Throws <see cref="LlmUnavailableException"/> when the underlying language model cannot be reached.
/// </summary>
public interface IWorkflowCodeGenerator
{
    /// <summary>
    /// Produces a complete Semantic Kernel process class for the supplied workflow, taking the
    /// full chat history into account so that earlier design decisions are respected.
    /// Each token is forwarded to <paramref name="onToken"/> as it arrives, enabling streaming UX.
    /// </summary>
    /// <param name="workflow">The canonical workflow definition describing steps, connections, and metadata.</param>
    /// <param name="chatHistory">Ordered conversation turns that capture prior design intent and constraints.</param>
    /// <param name="onToken">Callback invoked once per streamed token; must be thread-safe.</param>
    /// <param name="cancellationToken">Token used to cancel the streaming request mid-flight.</param>
    /// <returns>The fully assembled generated source code as a single string.</returns>
    /// <exception cref="LlmUnavailableException">Thrown when the language model endpoint is unreachable or returns a non-retriable error.</exception>
    Task<string> GenerateAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<WorkflowChatMessage> chatHistory,
        Action<string> onToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a natural-language <paramref name="instruction"/> to <paramref name="previousCode"/>,
    /// producing an updated source file together with a structured diff so callers can display
    /// exactly what changed without performing their own text comparison.
    /// Each token is forwarded to <paramref name="onToken"/> as it arrives, enabling streaming UX.
    /// </summary>
    /// <param name="previousCode">The last accepted generated source code that the refinement should build upon.</param>
    /// <param name="instruction">A natural-language description of the change the user wants applied.</param>
    /// <param name="workflow">The current workflow definition, which may have been modified since <paramref name="previousCode"/> was generated.</param>
    /// <param name="onToken">Callback invoked once per streamed token; must be thread-safe.</param>
    /// <param name="cancellationToken">Token used to cancel the streaming request mid-flight.</param>
    /// <returns>
    /// A tuple of the updated source code and a <see cref="DiffResult"/> that describes every
    /// addition, removal, and unchanged context line so the UI can render a diff view.
    /// </returns>
    /// <exception cref="LlmUnavailableException">Thrown when the language model endpoint is unreachable or returns a non-retriable error.</exception>
    Task<(string UpdatedCode, DiffResult Diff)> RefineAsync(
        string previousCode,
        string instruction,
        WorkflowDefinition workflow,
        Action<string> onToken,
        CancellationToken cancellationToken = default);
}
