// Exception carrying a list of plain-language validation messages for the workflow definition.

namespace DBAIAzure.Core.Exceptions;

/// <summary>
/// Thrown when <see cref="DBAIAzure.Core.Interfaces.IWorkflowValidator"/> finds one or more
/// structural problems with a workflow definition. Each message in <see cref="Messages"/> is
/// plain English, intended to be shown directly to the user without further transformation.
/// </summary>
public sealed class WorkflowValidationException : Exception
{
    /// <summary>
    /// Initialises the exception with a list of user-displayable validation messages.
    /// </summary>
    /// <param name="messages">One or more plain-language descriptions of what is wrong.</param>
    public WorkflowValidationException(IReadOnlyList<string> messages)
        : base(string.Join(" ", messages))
    {
        Messages = messages;
    }

    /// <summary>
    /// The individual validation messages describing each problem found.
    /// Displayed as amber banners in the builder UI; no stack traces or enum names shown to the user.
    /// </summary>
    public IReadOnlyList<string> Messages { get; }
}
