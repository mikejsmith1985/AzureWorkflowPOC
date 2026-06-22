// Shared hand-rolled fakes and builders for the Node Realization unit tests — keeps every test in the
// pure unit layer (no I/O, no network) per Article V.

using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;

namespace DBAIAzure.Tests;

/// <summary>
/// Fake <see cref="IStructuredCompletionService"/> that returns a canned, snake_case JSON payload per
/// forced-tool name and deserializes it onto the requested DTO exactly as the real Anthropic service
/// would — so the test exercises the realization service's real mapping and validation paths.
/// </summary>
internal sealed class CannedStructuredCompletionService : IStructuredCompletionService
{
    // Mirrors AnthropicChatCompletionService's binding policy so DTO property names line up.
    private static readonly JsonSerializerOptions BindingOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IReadOnlyDictionary<string, string> _responsesByToolName;

    /// <summary>The forced-tool names called, in order — lets a test assert per-node call sequencing.</summary>
    public List<string> ToolCalls { get; } = new();

    public CannedStructuredCompletionService(IReadOnlyDictionary<string, string> responsesByToolName)
    {
        _responsesByToolName = responsesByToolName;
    }

    public Task<T> GetStructuredAsync<T>(
        string systemPrompt, string userMessage, string toolName, string toolDescription,
        string inputSchemaJson, CancellationToken cancellationToken = default)
    {
        ToolCalls.Add(toolName);
        var json   = _responsesByToolName.TryGetValue(toolName, out var canned) ? canned : "{}";
        var result = JsonSerializer.Deserialize<T>(json, BindingOptions)
            ?? throw new InvalidOperationException($"Fake could not bind '{toolName}' to {typeof(T).Name}.");
        return Task.FromResult(result);
    }
}

/// <summary>Fake connector repository: reports a fixed LLM model name and no other configuration.</summary>
internal sealed class FakeConnectorConfigRepository : IConnectorConfigRepository
{
    private readonly string? _llmModelName;

    public FakeConnectorConfigRepository(string? llmModelName = "claude-test-model")
    {
        _llmModelName = llmModelName;
    }

    public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default)
    {
        if (type == ConnectorType.LLM && _llmModelName is not null)
        {
            var nonSecret = JsonSerializer.Serialize(new { modelName = _llmModelName });
            return Task.FromResult<ConnectorConfig?>(
                new ConnectorConfig(type, nonSecret, true, true, DateTimeOffset.UtcNow, null));
        }
        return Task.FromResult<ConnectorConfig?>(null);
    }

    public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());

    public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>Fake health checker: reports a configurable set of connectors as healthy, the rest failing.</summary>
internal sealed class FakeConnectorHealthChecker : IConnectorHealthChecker
{
    private readonly IReadOnlySet<ConnectorType> _healthy;

    public FakeConnectorHealthChecker(params ConnectorType[] healthy)
    {
        _healthy = healthy.ToHashSet();
    }

    public Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default) =>
        Task.FromResult(new ConnectorTestResult(type, _healthy.Contains(type), "fake", DateTimeOffset.UtcNow));

    public Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default)
    {
        var allTypes = Enum.GetValues<ConnectorType>();
        var results  = allTypes
            .Select(type => new ConnectorTestResult(type, _healthy.Contains(type), "fake", DateTimeOffset.UtcNow))
            .ToList();
        return Task.FromResult<IReadOnlyList<ConnectorTestResult>>(results);
    }
}

/// <summary>Builders for small, deterministic workflows used across the realization tests.</summary>
internal static class RealizationTestWorkflows
{
    /// <summary>A linear Trigger → AgenticReason → FunctionNotify workflow, fully wired, nothing realized.</summary>
    public static WorkflowDefinition TriggerAgentNotify()
    {
        var triggerOut = new WorkflowPort("begin", "Begin", PortDirection.Output);
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Ticket arrives") with
        {
            OutputPorts = new[] { triggerOut }.ToList().AsReadOnly(),
        };

        var agentIn  = new WorkflowPort("in", "Input", PortDirection.Input);
        var agentOut = new WorkflowPort("out", "Output", PortDirection.Output);
        var agent = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Triage the ticket") with
        {
            GoalPrompt  = "Decide the urgency of the incoming ticket.",
            InputPorts  = new[] { agentIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { agentOut }.ToList().AsReadOnly(),
        };

        var notifyIn = new WorkflowPort("in", "Input", PortDirection.Input);
        var notify = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Tell the on-call team") with
        {
            GoalPrompt  = "Send a Teams message to the on-call team.",
            InputPorts  = new[] { notifyIn }.ToList().AsReadOnly(),
        };

        var triggerToAgent = WorkflowEdge.CreateNew(trigger.Id, triggerOut.Id, agent.Id, agentIn.Id, "Begin");
        var agentToNotify  = WorkflowEdge.CreateNew(agent.Id, agentOut.Id, notify.Id, notifyIn.Id, "Output");

        return new WorkflowDefinition
        {
            Id             = Guid.NewGuid(),
            Name           = "Triage",
            OwnerId        = "owner1",
            Nodes          = new[] { trigger, agent, notify }.ToList().AsReadOnly(),
            Edges          = new[] { triggerToAgent, agentToNotify }.ToList().AsReadOnly(),
            Settings       = new WorkflowSettings(),
            ChatHistory    = Array.Empty<WorkflowChatMessage>(),
            CreatedAt      = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Attaches a realized config envelope to a node by id, marking it configured.</summary>
    public static WorkflowDefinition WithRealizedNode<TConfig>(
        this WorkflowDefinition workflow, string nodeId, WorkflowNodeType nodeType, TConfig config)
    {
        var functionConfig = NodeConfigSerializer.ToFunctionConfig(nodeType, config);
        var nodes = workflow.Nodes
            .Select(node => node.Id == nodeId
                ? node with { FunctionConfig = functionConfig, IsConfigured = true }
                : node)
            .ToList();
        return workflow with { Nodes = nodes };
    }
}
