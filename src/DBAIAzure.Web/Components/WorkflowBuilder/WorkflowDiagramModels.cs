// Blazor.Diagrams subclasses that bridge domain workflow models to the diagramming library's
// node, port, and link primitives used by the visual workflow canvas.


using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Components.WorkflowBuilder;

/// <summary>
/// Payload raised by WorkflowNodeRenderer when the user commits an inline label edit.
/// Carries the node identifier and the before/after label values so the canvas can
/// update the node model and record an undoable RenameLabelAction on the undo stack.
/// </summary>
public readonly record struct LabelCommitArgs(
    string NodeId,
    string PreviousLabel,
    string NewLabel);

/// <summary>
/// Blazor.Diagrams NodeModel subclass that carries the domain WorkflowNode.
/// Created by WorkflowCanvas on palette drop so the diagram layer can render and
/// position the tile while the domain record remains the authoritative source of truth.
/// </summary>
public sealed class WorkflowNodeModel : NodeModel
{
    /// <summary>
    /// True when this node has zero remaining connections after another node was deleted.
    /// Set by WorkflowCanvas after a deletion to trigger the amber island badge in WorkflowNodeRenderer.
    /// Reset when a new edge is added to this node.
    /// </summary>
    public bool IsIsolated { get; set; }

    /// <summary>
    /// True while the user is actively editing this node's label via the inline input.
    /// Set by WorkflowNodeRenderer on StartLabelEdit and cleared on CommitLabel/CancelLabel.
    /// WorkflowCanvas reads this flag to suppress the Delete/Backspace keyboard shortcut so
    /// typing in the input does not accidentally delete the node from the canvas.
    /// </summary>
    public bool IsLabelEditing { get; set; }
    /// <summary>
    /// Initialises the diagram node at the given canvas position and retains the
    /// domain record so that property panels and serialisation paths can read it
    /// without casting back through the diagram layer.
    /// </summary>
    /// <param name="workflowNode">The domain node this tile represents.</param>
    /// <param name="position">
    /// Initial canvas position; defaults to the origin when not supplied, which the
    /// canvas corrects immediately with the drop coordinates.
    /// </param>
    public WorkflowNodeModel(WorkflowNode workflowNode, Point? position = null)
        : base(position)
    {
        WorkflowNode = workflowNode;
    }

    /// <summary>
    /// The domain WorkflowNode that this diagram tile represents. All configuration,
    /// labelling, and port topology come from this record; the diagram model is a
    /// thin presentation wrapper over it.
    /// Settable so the config panel can push an updated record without replacing the node model.
    /// </summary>
    public WorkflowNode WorkflowNode { get; set; }

    /// <summary>
    /// Raised by WorkflowNodeRenderer when the user double-clicks the node tile.
    /// WorkflowCanvas subscribes to open the configuration panel.
    /// </summary>
    public event Action<WorkflowNodeModel>? DoubleClicked;

    /// <summary>Called by the renderer component on ondblclick to propagate the event to the canvas.</summary>
    public void RaiseDoubleClicked() => DoubleClicked?.Invoke(this);

    /// <summary>
    /// Raised by WorkflowNodeRenderer when the user right-clicks the node tile.
    /// WorkflowCanvas subscribes to open the context menu at the client coordinates.
    /// </summary>
    public event Action<WorkflowNodeModel, double, double>? ContextMenuRequested;

    /// <summary>Called by the renderer component on oncontextmenu to propagate the event to the canvas.</summary>
    public void RaiseContextMenuRequested(double clientX, double clientY) =>
        ContextMenuRequested?.Invoke(this, clientX, clientY);

    /// <summary>
    /// Raised by WorkflowNodeRenderer when the user commits an inline label edit.
    /// WorkflowCanvas subscribes in OnNodeAdded (alongside DoubleClicked) to receive
    /// before/after label values and record a RenameLabelAction on the undo stack.
    /// The renderer cannot pass this via EventCallback because it is instantiated by
    /// _diagram.RegisterComponent — it never appears directly in canvas razor markup.
    /// </summary>
    public event Action<string, string>? LabelCommitted;

    /// <summary>Called by the renderer component inside CommitLabel() to signal a committed label change.</summary>
    public void RaiseLabelCommitted(string previousLabel, string newLabel) =>
        LabelCommitted?.Invoke(previousLabel, newLabel);
}

/// <summary>
/// Port subclass that carries the domain WorkflowPort definition including its
/// plain-language label. Keeping the domain record on the port means edge-creation
/// callbacks can read the port label and direction without an additional lookup.
/// </summary>
public sealed class WorkflowPortModel : PortModel
{
    /// <summary>
    /// Initialises the diagram port on the given parent node at the specified alignment
    /// and retains the domain port record for downstream use by edge callbacks and
    /// the connector configuration modal.
    /// </summary>
    /// <param name="parent">The diagram node that owns this port.</param>
    /// <param name="workflowPort">The domain port definition this anchor represents.</param>
    /// <param name="alignment">
    /// Visual alignment on the node tile — typically <c>PortAlignment.Left</c> for
    /// input ports and <c>PortAlignment.Right</c> for output ports.
    /// </param>
    public WorkflowPortModel(WorkflowNodeModel parent, WorkflowPort workflowPort, PortAlignment alignment)
        : base(parent, alignment, null, null)
    {
        WorkflowPort = workflowPort;
    }

    /// <summary>
    /// The domain WorkflowPort that this anchor represents. Carries the human-readable
    /// label and direction so the canvas tooltip and connector modal can surface them
    /// without reaching back into the node's port lists.
    /// </summary>
    public WorkflowPort WorkflowPort { get; }
}

/// <summary>
/// Link subclass that carries the domain WorkflowEdge including its plain-language label.
/// Wrapping the domain record here lets the canvas renderer draw the edge label and lets
/// serialisation reconstruct the full edge definition from the diagram without a separate
/// lookup table.
/// </summary>
public sealed class WorkflowEdgeModel : LinkModel
{
    /// <summary>
    /// Initialises the diagram link between the given source and target ports and
    /// retains the domain edge record so that the label, IDs, and semantic meaning are
    /// always co-located with the visual connection.
    /// </summary>
    /// <param name="workflowEdge">The domain edge this link represents.</param>
    /// <param name="sourcePort">The output port the edge originates from.</param>
    /// <param name="targetPort">The input port the edge terminates at.</param>
    public WorkflowEdgeModel(WorkflowEdge workflowEdge, WorkflowPortModel sourcePort, WorkflowPortModel targetPort)
        : base(sourcePort, targetPort)
    {
        WorkflowEdge = workflowEdge;

        // Arrowhead at the target end: 20 × 14 px, which exceeds the FR-10.1 minimum of 12 × 8 px.
        TargetMarker = LinkMarker.NewArrow(20, 14);
    }

    /// <summary>
    /// The domain WorkflowEdge that this diagram link represents. The label stored here
    /// is rendered on the connecting arrow and also used by the serialiser to reconstruct
    /// the workflow definition without referencing the diagram model hierarchy.
    /// </summary>
    public WorkflowEdge WorkflowEdge { get; }

    /// <summary>
    /// When true, the execution-flow animation (a travelling cyan dot) plays on this edge.
    /// Set to true by WorkflowCanvas when execution reaches the source node; reset to false
    /// when execution leaves the target node. Drives the <c>edge-flow-active</c> CSS class.
    /// </summary>
    public bool IsAnimating { get; set; }
}
