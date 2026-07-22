// Unit tests for the node-config assembler (spec-021, node-config step 3): configuration edited ON a workflow
// node overrides the connector card, other namespaces are preserved, and any failure falls back safely.
// Pure — hand-rolled fakes, no infrastructure (Article V, <10ms).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Web.Services;
using DBAIAzure.Web.Services.Dor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class NodeAwareDorConfigResolverTests
{
    private const string NodeDocument = "# Definition of Ready\n1. Edited on the node.";

    [Fact]
    public async Task ResolveActive_NodeCarriesDorDocument_OverridesConnectorDocument()
    {
        var resolver = Build(
            connectorConfig: ConfiguredConfig(inlineMarkdown: "# From the connector card"),
            workflow: DorWorkflowWithDocument(NodeDocument));

        var config = await resolver.ResolveActiveAsync();

        Assert.Equal("inline", config.Dor.SourceType);
        Assert.Equal(NodeDocument, config.Dor.InlineMarkdown);
    }

    [Fact]
    public async Task ResolveActive_NodeDocument_BeatsUrlSourceOnTheCard()
    {
        // The node is becoming the source of truth: a document on the step wins over a stale card URL source.
        var connector = ConfiguredConfig() with
        {
            Dor = new DorDocConfig { SourceType = "url", SourceUri = "https://wiki/dor" },
        };
        var resolver = Build(connector, DorWorkflowWithDocument(NodeDocument));

        var config = await resolver.ResolveActiveAsync();

        Assert.Equal("inline", config.Dor.SourceType);
        Assert.Equal(NodeDocument, config.Dor.InlineMarkdown);
    }

    [Fact]
    public async Task ResolveActive_NoDorWorkflowStored_ReturnsConnectorConfigUnchanged()
    {
        var connector = ConfiguredConfig(inlineMarkdown: "# From the connector card");
        var resolver = Build(connector, workflow: null);

        var config = await resolver.ResolveActiveAsync();

        Assert.Equal("# From the connector card", config.Dor.InlineMarkdown);
    }

    [Fact]
    public async Task ResolveActive_NodeDocumentBlank_FallsBackToConnectorConfig()
    {
        var connector = ConfiguredConfig(inlineMarkdown: "# From the connector card");
        var resolver = Build(connector, DorWorkflowWithDocument("   "));

        var config = await resolver.ResolveActiveAsync();

        Assert.Equal("# From the connector card", config.Dor.InlineMarkdown);
    }

    [Fact]
    public async Task ResolveActive_UnrelatedWorkflow_IsIgnored()
    {
        var connector = ConfiguredConfig(inlineMarkdown: "# From the connector card");
        var unrelated = DorWorkflowWithDocument(NodeDocument) with { Name = "Some Other Workflow" };
        var resolver = Build(connector, unrelated);

        var config = await resolver.ResolveActiveAsync();

        Assert.Equal("# From the connector card", config.Dor.InlineMarkdown);
    }

    [Fact]
    public async Task ResolveActive_PreservesEveryOtherNamespace()
    {
        var connector = ConfiguredConfig(inlineMarkdown: "# card") with
        {
            Jira = new DorJiraConfig { ProjectKeys = new[] { "SBRO" }, ReadyTransitionId = "31" },
            Run = new DorRunConfig { DryRun = true },
        };
        var resolver = Build(connector, DorWorkflowWithDocument(NodeDocument));

        var config = await resolver.ResolveActiveAsync();

        // Only the DoR namespace is overlaid; the rest still comes from the connector row.
        Assert.Equal(new[] { "SBRO" }, config.Jira.ProjectKeys);
        Assert.Equal("31", config.Jira.ReadyTransitionId);
        Assert.True(config.Run.DryRun);
        Assert.True(config.IsConfigured);
        Assert.Equal(NodeDocument, config.Dor.InlineMarkdown);
    }

    [Fact]
    public async Task ResolveActive_RepositoryThrows_FallsBackToConnectorConfig()
    {
        var connector = ConfiguredConfig(inlineMarkdown: "# From the connector card");
        var resolver = new NodeAwareDorConfigResolver(
            new StubConfigResolver(connector), new ThrowingWorkflowRepository(),
            NullLogger<NodeAwareDorConfigResolver>.Instance);

        var config = await resolver.ResolveActiveAsync();

        Assert.Equal("# From the connector card", config.Dor.InlineMarkdown);
    }

    [Fact]
    public async Task ResolveSecrets_DelegatesToTheEncryptedStore()
    {
        // Secrets are never read from nodes — node config is plain text in the workflow graph (Article IX).
        var resolver = Build(ConfiguredConfig(), DorWorkflowWithDocument(NodeDocument));

        var secrets = await resolver.ResolveSecretsAsync();

        Assert.Equal("hook-secret", secrets.JiraWebhookSecret);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static NodeAwareDorConfigResolver Build(DorWorkflowConfig connectorConfig, WorkflowDefinition? workflow) =>
        new(new StubConfigResolver(connectorConfig),
            new StubWorkflowRepository(workflow),
            NullLogger<NodeAwareDorConfigResolver>.Instance);

    private static DorWorkflowConfig ConfiguredConfig(string inlineMarkdown = "# card") => new()
    {
        IsConfigured = true,
        Dor = new DorDocConfig { SourceType = "inline", InlineMarkdown = inlineMarkdown },
    };

    /// <summary>A DoR-named workflow whose AI review node carries the DoR document as a Document reference.</summary>
    private static WorkflowDefinition DorWorkflowWithDocument(string document)
    {
        var workflow = DefaultWorkflowProvider.BuildDorValidationWorkflow();
        var updatedNodes = workflow.Nodes
            .Select(node => node.NodeType != WorkflowNodeType.AgenticReason
                ? node
                : node with
                {
                    FunctionConfig = NodeReferenceConfig.Write(null, new[]
                    {
                        new NodeReference
                        {
                            Type = NodeReferenceType.Document,
                            Name = DorDocumentDefaults.ReferenceName,
                            Value = document,
                        },
                    }),
                })
            .ToList()
            .AsReadOnly();

        return workflow with { Nodes = updatedNodes };
    }

    private sealed class StubConfigResolver : IDorConfigResolver
    {
        private readonly DorWorkflowConfig _config;
        public StubConfigResolver(DorWorkflowConfig config) => _config = config;

        public Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default) => Task.FromResult(_config);

        public Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorWorkflowSecrets(null, "hook-secret", null, null));
    }

    /// <summary>Returns the single stored workflow for its owner; verifies the assembler scopes by owner.</summary>
    private sealed class StubWorkflowRepository : IWorkflowRepository
    {
        private readonly WorkflowDefinition? _workflow;
        public StubWorkflowRepository(WorkflowDefinition? workflow) => _workflow = workflow;

        public Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinition>>(
                _workflow is not null && _workflow.OwnerId == ownerId
                    ? new[] { _workflow }
                    : Array.Empty<WorkflowDefinition>());

        public Task<Guid> SaveAsync(WorkflowDefinition workflow, CancellationToken ct = default) =>
            Task.FromResult(workflow.Id);
        public Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken ct = default) =>
            Task.FromResult<WorkflowDefinition?>(null);
        public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<WorkflowDefinition?>(null);
        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string name, string ownerId, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class ThrowingWorkflowRepository : IWorkflowRepository
    {
        public Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken ct = default)
            => throw new InvalidOperationException("store unavailable");

        public Task<Guid> SaveAsync(WorkflowDefinition workflow, CancellationToken ct = default) =>
            Task.FromResult(workflow.Id);
        public Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken ct = default) =>
            Task.FromResult<WorkflowDefinition?>(null);
        public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<WorkflowDefinition?>(null);
        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string name, string ownerId, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
