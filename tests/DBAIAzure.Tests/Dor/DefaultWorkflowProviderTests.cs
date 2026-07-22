// Unit tests for the default builder workflow (spec-021 US5 / T060/T063): the Workflow Builder's starter is the
// Intelligent DoR Validation Workflow (not the old Support Request example), and its graph is valid + runnable.
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Web.Services;
using DBAIAzure.Web.Services.Dor;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DefaultWorkflowProviderTests
{
    [Fact]
    public void Default_AiReviewNode_CarriesDorDocumentReference()
    {
        var workflow = DefaultWorkflowProvider.BuildDorValidationWorkflow();

        var review = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason);
        var references = NodeReferenceConfig.Read(review.FunctionConfig);

        // The DoR document now lives on the AI review node itself, under the canonical name the assembler uses.
        var dorDocument = Assert.Single(references, reference => reference.Name == DorDocumentDefaults.ReferenceName);
        Assert.Equal(NodeReferenceType.Document, dorDocument.Type);
        Assert.False(string.IsNullOrWhiteSpace(dorDocument.Value));
        Assert.Contains("Definition of Ready", dorDocument.Value);
    }

    [Fact]
    public void DorSampleDocument_ComesFromTheSharedDefault()
    {
        // The node seeds its document from the one shared default; the config card that used to duplicate it
        // has been retired, so DorDocumentDefaults is now the only source.
        var workflow = DefaultWorkflowProvider.BuildDorValidationWorkflow();
        var review = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason);
        var document = NodeReferenceConfig.Read(review.FunctionConfig)
            .Single(reference => reference.Name == DorDocumentDefaults.ReferenceName);

        Assert.Equal(DorDocumentDefaults.SampleMarkdown, document.Value);
    }

    [Fact]
    public void Default_IsTheDorWorkflow_NotTheSupportExample()
    {
        var workflow = DefaultWorkflowProvider.BuildDorValidationWorkflow();

        Assert.Equal("Intelligent DoR Validation Workflow", workflow.Name);
        Assert.DoesNotContain("Support Request", workflow.Name);
    }

    [Fact]
    public void Default_HasTriggerReviewAndHumanNodes_AllConfigured()
    {
        var workflow = DefaultWorkflowProvider.BuildDorValidationWorkflow();

        Assert.Single(workflow.Nodes, n => n.NodeType == WorkflowNodeType.Trigger);
        Assert.Contains(workflow.Nodes, n => n.NodeType == WorkflowNodeType.AgenticReason);   // AI DoR review
        Assert.Contains(workflow.Nodes, n => n.NodeType == WorkflowNodeType.HumanApproval);    // HITL conversation
        Assert.All(workflow.Nodes, n => Assert.True(n.IsConfigured, $"Node '{n.Label}' should ship configured."));
    }

    [Fact]
    public void Default_HasValidTopology_AndPassesValidation()
    {
        var workflow = DefaultWorkflowProvider.BuildDorValidationWorkflow();

        var nodeIds = workflow.Nodes.Select(n => n.Id).ToHashSet();
        Assert.NotEmpty(workflow.Edges);
        Assert.All(workflow.Edges, edge =>
        {
            Assert.Contains(edge.SourceNodeId, nodeIds);
            Assert.Contains(edge.TargetNodeId, nodeIds);
        });

        // Every non-trigger node is reachable from the trigger (no orphan nodes in the starter graph).
        var trigger = workflow.Nodes.Single(n => n.NodeType == WorkflowNodeType.Trigger);
        var reachable = Reachable(trigger.Id, workflow);
        Assert.All(workflow.Nodes, n => Assert.Contains(n.Id, reachable));

        workflow.ThrowIfInvalid(); // ≤1 trigger etc. — must not throw
    }

    private static HashSet<string> Reachable(string startNodeId, WorkflowDefinition workflow)
    {
        var reachable = new HashSet<string> { startNodeId };
        var queue = new Queue<string>();
        queue.Enqueue(startNodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in workflow.Edges.Where(e => e.SourceNodeId == current))
                if (reachable.Add(edge.TargetNodeId))
                    queue.Enqueue(edge.TargetNodeId);
        }
        return reachable;
    }
}
