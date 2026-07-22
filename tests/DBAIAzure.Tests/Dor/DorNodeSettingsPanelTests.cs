// bUnit tests for the DoR step-settings section of WorkflowNodeConfigPanel (node-config step 4b): each
// DoR node shows only the settings it owns, ordinary nodes show none, and edits round-trip into the node.
using Bunit;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Web.Components.WorkflowBuilder;
using DBAIAzure.Web.Services;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorNodeSettingsPanelTests : TestContext
{
    public DorNodeSettingsPanelTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void OrdinaryNode_ShowsNoDorSettings()
    {
        // A node with no DoR role must be unaffected — this panel serves every workflow, not just the DoR one.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Plain step");

        var cut = Render(node);

        Assert.Empty(cut.FindAll("[data-testid=\"dor-node-settings\"]"));
    }

    [Fact]
    public void TriggerNode_ShowsWatchedTicketFields_PrePopulated()
    {
        var trigger = StarterNode(DorNodeRole.Trigger);

        var cut = Render(trigger);

        Assert.Single(cut.FindAll("[data-testid=\"dor-node-settings\"]"));
        Assert.Equal("SBRO", cut.Find("[data-testid=\"dor-project-keys\"]").GetAttribute("value"));
        Assert.Contains("acceptance_criteria", cut.Find("[data-testid=\"dor-watch-fields\"]").GetAttribute("value"));
        // Fields belonging to other roles must not leak onto this node.
        Assert.Empty(cut.FindAll("[data-testid=\"dor-transition-id\"]"));
    }

    [Fact]
    public void ReadyTransitionNode_ShowsOnlyItsOwnFields()
    {
        var cut = Render(StarterNode(DorNodeRole.ReadyTransition));

        Assert.Equal("31", cut.Find("[data-testid=\"dor-transition-id\"]").GetAttribute("value"));
        Assert.Equal("Ready to Work", cut.Find("[data-testid=\"dor-ready-status\"]").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid=\"dor-project-keys\"]"));
    }

    [Fact]
    public void EscalateNode_ShowsChannelSlaAndManualLabel()
    {
        var cut = Render(StarterNode(DorNodeRole.Escalate));

        Assert.Equal("8", cut.Find("[data-testid=\"dor-sla-hours\"]").GetAttribute("value"));
        Assert.Equal("dor-manual-required", cut.Find("[data-testid=\"dor-manual-label\"]").GetAttribute("value"));
        // The escalation step has no conversation prompt — that belongs to the resolve step.
        Assert.Empty(cut.FindAll("[data-testid=\"dor-conversation-prompt\"]"));
    }

    [Fact]
    public void UpdateNode_ShowsTheWriteWhitelist()
    {
        var cut = Render(StarterNode(DorNodeRole.Update));

        Assert.Equal("acceptance_criteria", cut.Find("[data-testid=\"dor-editable-fields\"]").GetAttribute("value"));
    }

    [Fact]
    public void EditingASetting_RoundTripsIntoTheSavedNode()
    {
        var node = StarterNode(DorNodeRole.ReadyTransition);
        WorkflowNode? saved = null;

        var cut = RenderComponent<WorkflowNodeConfigPanel>(parameters => parameters
            .Add(panel => panel.Node, node)
            .Add(panel => panel.IsOpen, true)
            .Add(panel => panel.NodeUpdated, updated => saved = updated));

        cut.Find("[data-testid=\"dor-transition-id\"]").Input("41");
        cut.Find("[data-testid=\"dor-ready-status\"]").Input("Ready for Dev");
        cut.Find("button[aria-label=\"Confirm node configuration and close panel\"]").Click();

        Assert.NotNull(saved);
        var settings = DorNodeSettingsConfig.Read(saved!.FunctionConfig)!;
        Assert.Equal(DorNodeRole.ReadyTransition, settings.Role);
        Assert.Equal("41", settings.ReadyTransitionId);
        Assert.Equal("Ready for Dev", settings.ReadyStatus);
    }

    [Fact]
    public void SavingTheReviewNode_KeepsBothItsSettingsAndItsDorDocument()
    {
        // The review node carries a reference AND settings in one blob — saving must not drop either.
        var review = StarterNode(DorNodeRole.Review);
        WorkflowNode? saved = null;

        var cut = RenderComponent<WorkflowNodeConfigPanel>(parameters => parameters
            .Add(panel => panel.Node, review)
            .Add(panel => panel.IsOpen, true)
            .Add(panel => panel.NodeUpdated, updated => saved = updated));

        cut.Find("[data-testid=\"dor-max-tokens\"]").Input("3000");
        cut.Find("button[aria-label=\"Confirm node configuration and close panel\"]").Click();

        Assert.NotNull(saved);
        Assert.Equal(3000, DorNodeSettingsConfig.Read(saved!.FunctionConfig)!.MaxTokens);
        var references = NodeReferenceConfig.Read(saved.FunctionConfig);
        Assert.Contains(references, reference => reference.Name == DorDocumentDefaults.ReferenceName);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private IRenderedComponent<WorkflowNodeConfigPanel> Render(WorkflowNode node) =>
        RenderComponent<WorkflowNodeConfigPanel>(parameters => parameters
            .Add(panel => panel.Node, node)
            .Add(panel => panel.IsOpen, true));

    /// <summary>Pulls the real starter workflow's node for a role, so tests exercise the shipped defaults.</summary>
    private static WorkflowNode StarterNode(DorNodeRole role) =>
        DefaultWorkflowProvider.BuildDorValidationWorkflow().Nodes
            .Single(node => DorNodeSettingsConfig.ReadRole(node.FunctionConfig) == role);
}
