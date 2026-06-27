// Defines the contract for generating a compact SVG thumbnail from a workflow definition.

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Generates a compact SVG schematic thumbnail from the node layout of a workflow.
/// The thumbnail is stored on the <see cref="WorkflowDefinition"/> and displayed on
/// gallery cards to give users a visual preview without opening the builder.
/// </summary>
public interface IWorkflowThumbnailGenerator
{
    /// <summary>
    /// Produces an inline SVG string that schematically represents the workflow's node layout.
    /// Returns <see langword="null"/> when generation is not possible (e.g., zero nodes);
    /// callers must treat <see langword="null"/> as "no thumbnail available" and proceed
    /// without surfacing an error.
    /// </summary>
    /// <param name="workflow">The workflow definition whose nodes and edges are rendered.</param>
    /// <returns>
    /// An inline SVG string fitting a 200 × 100 viewBox, or <see langword="null"/>
    /// if the workflow has no nodes or generation fails.
    /// </returns>
    string? GenerateSvg(WorkflowDefinition workflow);
}
