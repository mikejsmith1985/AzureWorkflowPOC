// Unit tests for DoR rules and WorkflowPreRunValidator (T073-T077).
// All rule checks are pure domain logic — no infrastructure needed.

using DBAIAzure.Core.Configuration;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Web.Rules;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DBAIAzure.Tests;

// ── T073: TriggerNodePresentRule ─────────────────────────────────────────────

public sealed class TriggerNodePresentRuleTests
{
    private static readonly TriggerNodePresentRule Rule = new();
    private static readonly IReadOnlyList<ConnectorConfig> NoConnectors = Array.Empty<ConnectorConfig>();

    [Fact]
    public async Task CheckAsync_WorkflowHasTriggerNode_Passes()
    {
        var workflow = DorTestHelpers.WorkflowWith(WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start"));
        var result   = await Rule.CheckAsync(workflow, NoConnectors);
        Assert.True(result.Passed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task CheckAsync_WorkflowHasNoTriggerNode_Fails()
    {
        var workflow = DorTestHelpers.WorkflowWith(WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step"));
        var result   = await Rule.CheckAsync(workflow, NoConnectors);
        Assert.False(result.Passed);
        Assert.NotNull(result.FailureReason);
        Assert.Equal("trigger-node-present", result.RuleName);
    }

    [Fact]
    public async Task CheckAsync_EmptyWorkflow_Fails()
    {
        var result = await Rule.CheckAsync(DorTestHelpers.EmptyWorkflow(), NoConnectors);
        Assert.False(result.Passed);
    }
}

// ── T074: AllNodesRealizedRule ────────────────────────────────────────────────

public sealed class AllNodesRealizedRuleTests
{
    private static readonly AllNodesRealizedRule Rule = new();
    private static readonly IReadOnlyList<ConnectorConfig> NoConnectors = Array.Empty<ConnectorConfig>();

    [Fact]
    public async Task CheckAsync_AllNodesConfigured_Passes()
    {
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var step    = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step") with { IsConfigured = true };
        var result  = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(trigger, step), NoConnectors);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_NonTriggerNodeNotConfigured_Fails()
    {
        var trigger    = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var unrealized = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Unrealized");
        var result     = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(trigger, unrealized), NoConnectors);
        Assert.False(result.Passed);
        Assert.Contains("Unrealized", result.FailureReason);
    }

    [Fact]
    public async Task CheckAsync_TriggerNodeAlone_Passes()
    {
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var result  = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(trigger), NoConnectors);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_MoreThanThreeUnrealizedNodes_SuffixAppended()
    {
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var nodes   = Enumerable.Range(1, 5)
            .Select(i => WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, $"Step {i}"))
            .ToArray();
        var result  = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(new[] { trigger }.Concat(nodes).ToArray()), NoConnectors);
        Assert.False(result.Passed);
        Assert.Contains("+2 more", result.FailureReason);
    }
}

// ── T075: ConnectorsHealthyRule ───────────────────────────────────────────────

public sealed class ConnectorsHealthyRuleTests
{
    private static readonly ConnectorsHealthyRule Rule = new();

    [Fact]
    public async Task CheckAsync_NoConnectors_Passes()
    {
        var result = await Rule.CheckAsync(DorTestHelpers.EmptyWorkflow(), Array.Empty<ConnectorConfig>());
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_AllConnectorsHealthy_Passes()
    {
        var connectors = new[] { DorTestHelpers.ConnectorWithResult(ConnectorType.AzureDevOps, isSuccess: true) };
        var result     = await Rule.CheckAsync(DorTestHelpers.EmptyWorkflow(), connectors);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_OneUnhealthyConnector_Fails()
    {
        var connectors = new[] { DorTestHelpers.ConnectorWithResult(ConnectorType.AzureDevOps, isSuccess: false) };
        var result     = await Rule.CheckAsync(DorTestHelpers.EmptyWorkflow(), connectors);
        Assert.False(result.Passed);
        Assert.Contains("AzureDevOps", result.FailureReason);
        Assert.Equal("connectors-healthy", result.RuleName);
    }

    [Fact]
    public async Task CheckAsync_UnconfiguredConnector_IsIgnored()
    {
        var unconfigured = new ConnectorConfig(ConnectorType.Teams, null, false, false,
            DateTimeOffset.UtcNow, null);
        var result = await Rule.CheckAsync(DorTestHelpers.EmptyWorkflow(), new[] { unconfigured });
        Assert.True(result.Passed);
    }
}

// ── T076: ApprovalNodesConfiguredRule ─────────────────────────────────────────

public sealed class ApprovalNodesConfiguredRuleTests
{
    private static readonly ApprovalNodesConfiguredRule Rule = new();
    private static readonly IReadOnlyList<ConnectorConfig> NoConnectors = Array.Empty<ConnectorConfig>();

    [Fact]
    public async Task CheckAsync_NoApprovalNodes_Passes()
    {
        var workflow = DorTestHelpers.WorkflowWith(WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start"));
        var result   = await Rule.CheckAsync(workflow, NoConnectors);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_ApprovalNodeWithApproverChain_Passes()
    {
        var config = new ApprovalNodeConfig { PromptShown = "Approve?", ApproverChain = new[] { "alice@example.com" } };
        var node   = DorTestHelpers.ApprovalNodeWith(config);
        var result = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(node), NoConnectors);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_ApprovalNodeWithLegacyApprover_Passes()
    {
        var config = new ApprovalNodeConfig { PromptShown = "Approve?", Approver = "bob@example.com" };
        var node   = DorTestHelpers.ApprovalNodeWith(config);
        var result = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(node), NoConnectors);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task CheckAsync_ApprovalNodeWithNoApprover_Fails()
    {
        var config = new ApprovalNodeConfig { PromptShown = "Approve?" };
        var node   = DorTestHelpers.ApprovalNodeWith(config, label: "Needs Review");
        var result = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(node), NoConnectors);
        Assert.False(result.Passed);
        Assert.Contains("Needs Review", result.FailureReason);
        Assert.Equal("approval-nodes-configured", result.RuleName);
    }

    [Fact]
    public async Task CheckAsync_ApprovalNodeWithNullFunctionConfig_Fails()
    {
        var node   = WorkflowNode.CreateNew(WorkflowNodeType.HumanApproval, "No Config");
        var result = await Rule.CheckAsync(DorTestHelpers.WorkflowWith(node), NoConnectors);
        Assert.False(result.Passed);
    }
}

// ── T077: WorkflowPreRunValidator ─────────────────────────────────────────────

public sealed class WorkflowPreRunValidatorTests
{
    [Fact]
    public async Task ValidateAsync_DisabledRuleByName_IsSkipped()
    {
        var settings  = DorTestHelpers.SettingsOf(new DorRuleSettings { DisabledRuleNames = new[] { "always-fail" } });
        var validator = DorTestHelpers.BuildValidator(settings, new AlwaysFailRule("always-fail"));

        var results = await validator.ValidateAsync(DorTestHelpers.EmptyWorkflow());

        Assert.Empty(results);
    }

    [Fact]
    public async Task ValidateAsync_FailingRulesSortedFirst()
    {
        var validator = DorTestHelpers.BuildValidator(
            new AlwaysPassRule("pass"),
            new AlwaysFailRule("fail"));

        var results = await validator.ValidateAsync(DorTestHelpers.EmptyWorkflow());

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Passed);
        Assert.True(results[1].Passed);
    }

    [Fact]
    public async Task ValidateAsync_RuleThatThrows_ReturnedAsFailure()
    {
        var validator = DorTestHelpers.BuildValidator(new ExplodingRule("boom"));

        var results = await validator.ValidateAsync(DorTestHelpers.EmptyWorkflow());

        Assert.Single(results);
        Assert.False(results[0].Passed);
        Assert.Equal("boom", results[0].RuleName);
    }

    [Fact]
    public async Task ValidateAsync_AllRulesPass_NoFailures()
    {
        var validator = DorTestHelpers.BuildValidator(
            new AlwaysPassRule("r1"),
            new AlwaysPassRule("r2"));

        var results = await validator.ValidateAsync(DorTestHelpers.EmptyWorkflow());

        Assert.All(results, r => Assert.True(r.Passed));
    }

    // ── Fakes ────────────────────────────────────────────────────────────────────

    private sealed class AlwaysPassRule(string name) : IWorkflowReadinessRule
    {
        public string RuleName    => name;
        public string Description => "Always passes.";
        public Task<DorRuleResult> CheckAsync(WorkflowDefinition w, IReadOnlyList<ConnectorConfig> c, CancellationToken ct = default) =>
            Task.FromResult(new DorRuleResult(Passed: true, RuleName: name, FailureReason: null));
    }

    private sealed class AlwaysFailRule(string name) : IWorkflowReadinessRule
    {
        public string RuleName    => name;
        public string Description => "Always fails.";
        public Task<DorRuleResult> CheckAsync(WorkflowDefinition w, IReadOnlyList<ConnectorConfig> c, CancellationToken ct = default) =>
            Task.FromResult(new DorRuleResult(Passed: false, RuleName: name, FailureReason: "Always fails."));
    }

    private sealed class ExplodingRule(string name) : IWorkflowReadinessRule
    {
        public string RuleName    => name;
        public string Description => "Throws.";
        public Task<DorRuleResult> CheckAsync(WorkflowDefinition w, IReadOnlyList<ConnectorConfig> c, CancellationToken ct = default) =>
            Task.FromException<DorRuleResult>(new InvalidOperationException("Simulated rule failure."));
    }
}

// ── File-local helpers shared across all DoR test classes ────────────────────

file static class DorTestHelpers
{
    public static WorkflowDefinition EmptyWorkflow() =>
        WorkflowDefinition.CreateNew("Test", "owner");

    public static WorkflowDefinition WorkflowWith(params WorkflowNode[] nodes)
    {
        var wf = EmptyWorkflow();
        return wf with { Nodes = nodes.ToList().AsReadOnly() };
    }

    public static ConnectorConfig ConnectorWithResult(ConnectorType type, bool isSuccess) =>
        new(Type: type,
            NonSecretConfig: "{}",
            HasSecrets: true,
            IsConfigured: true,
            LastUpdatedAt: DateTimeOffset.UtcNow,
            LastTestResult: new ConnectorTestResult(type, isSuccess, isSuccess ? "OK" : "Error", DateTimeOffset.UtcNow));

    public static WorkflowNode ApprovalNodeWith(ApprovalNodeConfig config, string label = "Approval") =>
        WorkflowNode.CreateNew(WorkflowNodeType.HumanApproval, label) with
        {
            // NodeConfigSerializer wraps the config in an envelope; ReadConfig expects that format.
            FunctionConfig = NodeConfigSerializer.ToFunctionConfig(WorkflowNodeType.HumanApproval, config),
        };

    public static IOptions<DorRuleSettings> SettingsOf(DorRuleSettings s) =>
        Options.Create(s);

    public static WorkflowPreRunValidator BuildValidator(params IWorkflowReadinessRule[] rules) =>
        new(rules,
            SettingsOf(new DorRuleSettings()),
            new NullConnectorRepo(),
            NullLogger<WorkflowPreRunValidator>.Instance);

    public static WorkflowPreRunValidator BuildValidator(IOptions<DorRuleSettings> settings, params IWorkflowReadinessRule[] rules) =>
        new(rules, settings, new NullConnectorRepo(), NullLogger<WorkflowPreRunValidator>.Instance);

    // Null object — DoR validator loads connectors for rule evaluation.
    internal sealed class NullConnectorRepo : IConnectorConfigRepository
    {
        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<ConnectorConfig?>(null);
        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());
        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) => Task.CompletedTask;
    }
}
