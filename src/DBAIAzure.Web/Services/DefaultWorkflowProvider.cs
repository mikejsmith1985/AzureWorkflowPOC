// Builds the default starter workflow shown in the Workflow Builder (spec-021 US5): the Intelligent DoR
// Validation Workflow, which replaces the old "Support Request Flow" example. Each node maps to an existing
// builder node type (the human-conversation node is a HumanApproval, which realizes to a MAF RequestPort).
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Produces the canonical DoR Validation Workflow graph an operator starts from. The graph is a faithful visual
/// representation of the executable workflow: trigger → AI DoR review → ready? → (ready) transition, or
/// (not ready) resolve gaps with a human → update / escalate → audit. All nodes ship configured so the canvas
/// shows no amber badges and the workflow is immediately runnable.
/// </summary>
public static class DefaultWorkflowProvider
{
    /// <summary>The default workflow's display name.</summary>
    public const string DefaultName = "Intelligent DoR Validation Workflow";

    private const string DemoOwnerId = "demo";

    /// <summary>Builds a fresh DoR Validation Workflow definition (new ids each call).</summary>
    public static WorkflowDefinition BuildDorValidationWorkflow()
    {
        static WorkflowPort Port(string label, PortDirection direction) =>
            new(Guid.NewGuid().ToString("N")[..8], label, direction);

        // 1. Trigger — a Jira ticket is created.
        var triggerOut = Port("New ticket", PortDirection.Output);
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Jira Ticket Created") with
        {
            GoalPrompt = "A ticket is created in the monitored Jira project — start the Definition-of-Ready check.",
            OutputLabel = triggerOut.Label,
            OutputPorts = new[] { triggerOut }.ToList().AsReadOnly(),
            IsConfigured = true, PositionX = 60, PositionY = 200,
        };

        // 2. AI DoR review.
        var reviewIn = Port("Ticket", PortDirection.Input);
        var reviewOut = Port("DoR verdict", PortDirection.Output);
        var review = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "AI DoR Review") with
        {
            GoalPrompt = "Evaluate the ticket against the current Definition of Ready document and return a pass/fail verdict with the gaps.",
            InputLabel = reviewIn.Label, OutputLabel = reviewOut.Label,
            InputPorts = new[] { reviewIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { reviewOut }.ToList().AsReadOnly(),
            // The DoR document lives ON this node as a Document reference — open the node to read or edit it.
            // Stored under the canonical name so the node-config assembler (a later step) resolves it here
            // instead of from a separate connector card.
            FunctionConfig = NodeReferenceConfig.Write(null, new[]
            {
                new NodeReference
                {
                    Type = NodeReferenceType.Document,
                    Name = DorDocumentDefaults.ReferenceName,
                    Value = DorDocumentDefaults.SampleMarkdown,
                },
            }),
            IsConfigured = true, PositionX = 300, PositionY = 200,
        };

        // 3. Route on the verdict.
        var routeIn = Port("Verdict", PortDirection.Input);
        var routeReady = Port("Ready", PortDirection.Output);
        var routeNotReady = Port("Not ready", PortDirection.Output);
        var route = WorkflowNode.CreateNew(WorkflowNodeType.FunctionRoute, "Ready to Work?") with
        {
            GoalPrompt = "Route ready tickets to auto-advance; route not-ready tickets to the human conversation.",
            InputLabel = routeIn.Label,
            InputPorts = new[] { routeIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { routeReady, routeNotReady }.ToList().AsReadOnly(),
            IsConfigured = true, PositionX = 540, PositionY = 200,
        };

        // 4. Ready path — transition the ticket.
        var transitionIn = Port("Ready ticket", PortDirection.Input);
        var transitionOut = Port("Transitioned", PortDirection.Output);
        var transition = WorkflowNode.CreateNew(WorkflowNodeType.FunctionData, "Move to Ready Status") with
        {
            GoalPrompt = "Transition the ticket to the configured ready status and post a success notice.",
            InputLabel = transitionIn.Label, OutputLabel = transitionOut.Label,
            InputPorts = new[] { transitionIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { transitionOut }.ToList().AsReadOnly(),
            IsConfigured = true, PositionX = 800, PositionY = 90,
        };

        // 5. Not-ready path — resolve the gaps with a human (the HITL conversation → RequestPort).
        var resolveIn = Port("Gaps", PortDirection.Input);
        var resolveResolved = Port("Resolved", PortDirection.Output);
        var resolveEscalate = Port("SLA breach", PortDirection.Output);
        var resolve = WorkflowNode.CreateNew(WorkflowNodeType.HumanApproval, "Resolve Gaps in Chat") with
        {
            GoalPrompt = "Open a chat conversation to close the DoR gaps; re-evaluate each reply until resolved or the SLA/limit is hit.",
            InputLabel = resolveIn.Label, OutputLabel = resolveResolved.Label,
            InputPorts = new[] { resolveIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { resolveResolved, resolveEscalate }.ToList().AsReadOnly(),
            IsConfigured = true, PositionX = 800, PositionY = 300,
        };

        // 6. Apply the resolution — update whitelisted fields + transition.
        var updateIn = Port("Resolution", PortDirection.Input);
        var updateOut = Port("Updated", PortDirection.Output);
        var update = WorkflowNode.CreateNew(WorkflowNodeType.FunctionData, "Update Ticket & Transition") with
        {
            GoalPrompt = "Write the resolved fields (whitelist only) and transition the ticket to ready.",
            InputLabel = updateIn.Label, OutputLabel = updateOut.Label,
            InputPorts = new[] { updateIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { updateOut }.ToList().AsReadOnly(),
            IsConfigured = true, PositionX = 1060, PositionY = 230,
        };

        // 7. Escalation / manual handoff.
        var escalateIn = Port("Unresolved", PortDirection.Input);
        var escalateOut = Port("Handed off", PortDirection.Output);
        var escalate = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Escalate / Manual Handoff") with
        {
            GoalPrompt = "On SLA breach or exhausted iterations, escalate to the escalation channel and tag the ticket for manual action.",
            InputLabel = escalateIn.Label, OutputLabel = escalateOut.Label,
            InputPorts = new[] { escalateIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { escalateOut }.ToList().AsReadOnly(),
            IsConfigured = true, PositionX = 1060, PositionY = 380,
        };

        // 8. Audit & close (terminal).
        var auditIn = Port("Outcome", PortDirection.Input);
        var audit = WorkflowNode.CreateNew(WorkflowNodeType.FunctionTransform, "Audit & Close") with
        {
            GoalPrompt = "Record the outcome (passed / resolved / manual-required) to the append-only audit trail and close the run.",
            InputLabel = auditIn.Label,
            InputPorts = new[] { auditIn }.ToList().AsReadOnly(),
            IsConfigured = true, PositionX = 1320, PositionY = 300,
        };

        var edges = new[]
        {
            WorkflowEdge.CreateNew(trigger.Id, triggerOut.Id, review.Id, reviewIn.Id, "Ticket created"),
            WorkflowEdge.CreateNew(review.Id, reviewOut.Id, route.Id, routeIn.Id, "Verdict"),
            WorkflowEdge.CreateNew(route.Id, routeReady.Id, transition.Id, transitionIn.Id, "Ready"),
            WorkflowEdge.CreateNew(route.Id, routeNotReady.Id, resolve.Id, resolveIn.Id, "Not ready"),
            WorkflowEdge.CreateNew(transition.Id, transitionOut.Id, audit.Id, auditIn.Id, "Passed"),
            WorkflowEdge.CreateNew(resolve.Id, resolveResolved.Id, update.Id, updateIn.Id, "Resolved"),
            WorkflowEdge.CreateNew(resolve.Id, resolveEscalate.Id, escalate.Id, escalateIn.Id, "SLA breach"),
            WorkflowEdge.CreateNew(update.Id, updateOut.Id, audit.Id, auditIn.Id, "Resolved-auto"),
            WorkflowEdge.CreateNew(escalate.Id, escalateOut.Id, audit.Id, auditIn.Id, "Manual-required"),
        }.ToList().AsReadOnly();

        return new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Name = DefaultName,
            OwnerId = DemoOwnerId,
            Nodes = new[] { trigger, review, route, transition, resolve, update, escalate, audit }.ToList().AsReadOnly(),
            Edges = edges,
            Settings = new WorkflowSettings(),
            ChatHistory = Array.Empty<WorkflowChatMessage>(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };
    }
}
