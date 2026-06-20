// Defines the chat message model used by the Visual Workflow Builder chat panel.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Identifies which participant in a workflow chat conversation produced a message —
/// either the human operator or the AI assistant.
/// </summary>
public enum ChatRole
{
    /// <summary>The human operator driving the workflow conversation.</summary>
    User,

    /// <summary>The AI assistant responding to workflow queries.</summary>
    Assistant,
}

/// <summary>
/// Represents a single, immutable message exchanged in a workflow chat session.
/// Carrying the role, content, and rendering hints needed to display it faithfully
/// in the Visual Workflow Builder chat panel.
/// </summary>
/// <param name="Role">Whether the message was produced by the user or the AI assistant.</param>
/// <param name="Content">The raw text (or code) content of the message.</param>
/// <param name="IsCodeBlock">
/// <see langword="true"/> when <paramref name="Content"/> is a generated code snippet
/// that should receive syntax-highlight rendering in the UI; <see langword="false"/> for
/// plain conversational prose.
/// </param>
/// <param name="Timestamp">The UTC-offset instant at which the message was created.</param>
public sealed record WorkflowChatMessage(
    ChatRole Role,
    string Content,
    bool IsCodeBlock,
    DateTimeOffset Timestamp);
