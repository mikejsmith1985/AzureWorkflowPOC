// Contract for validating the structural integrity of a workflow definition.

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Validates the structural invariants of a workflow definition before it is
/// persisted or executed. The Web layer injects this to display user-friendly
/// messages without catching exceptions from the domain.
/// </summary>
public interface IWorkflowValidator
{
    /// <summary>
    /// Checks the workflow for structural problems and returns a list of plain-language
    /// messages describing each problem found. An empty list means the workflow is
    /// structurally valid and safe to run.
    /// Messages are written for non-technical users — no stack traces, no enum names.
    /// </summary>
    /// <param name="definition">The workflow to validate.</param>
    /// <returns>
    /// A list of zero or more plain-language problem descriptions.
    /// Empty = valid; non-empty = show messages to the user and block the action.
    /// </returns>
    IReadOnlyList<string> Validate(WorkflowDefinition definition);
}
