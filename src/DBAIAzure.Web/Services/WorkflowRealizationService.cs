// Turns plain-language workflow nodes into executable per-node configuration using the LLM, and
// applies user-accepted proposals to a workflow. Proposing is read-only; acceptance is a separate,
// deterministic, no-LLM step (contracts/realization-service.md C1).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Core.Validation;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Default <see cref="IWorkflowRealizationService"/>. For each node it asks the LLM (via forced,
/// schema-bound tool use) for the executable configuration that matches the user's plain-language
/// intent, then surfaces it as a reviewable <see cref="RealizationProposal"/>. It never mutates the
/// workflow while proposing; <see cref="AcceptProposal"/> is the only mutation and performs no LLM call.
/// </summary>
public sealed class WorkflowRealizationService : IWorkflowRealizationService
{
    // Fallback model identifier used for agentic steps when the LLM connector has no model name saved.
    private const string DefaultModelRef = "claude-sonnet-4-6";

    private readonly IStructuredCompletionService _structuredService;
    private readonly IConnectorConfigRepository _connectorRepo;

    /// <summary>Initialises the service with the structured-output LLM seam and the connector repository.</summary>
    public WorkflowRealizationService(
        IStructuredCompletionService structuredService,
        IConnectorConfigRepository connectorRepo)
    {
        _structuredService = structuredService;
        _connectorRepo     = connectorRepo;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RealizationProposal>> ProposeAllAsync(
        WorkflowDefinition workflow,
        Action<RealizationProposal> onProposal,
        CancellationToken cancellationToken = default)
    {
        var modelRef  = await ResolveModelRefAsync(cancellationToken).ConfigureAwait(false);
        var proposals = new List<RealizationProposal>();

        // Propose in graph order (trigger-first) so the user reviews the flow the way it runs.
        foreach (var node in OrderNodesForRealization(workflow))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proposal = await ProposeForNodeAsync(workflow, node, modelRef, cancellationToken)
                .ConfigureAwait(false);
            proposals.Add(proposal);
            onProposal(proposal);
        }

        return proposals;
    }

    /// <inheritdoc/>
    public async Task<RealizationProposal> ProposeNodeAsync(
        WorkflowDefinition workflow,
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var node = workflow.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId)
            ?? throw new ArgumentException($"No node with id '{nodeId}' exists in this workflow.", nameof(nodeId));

        var modelRef = await ResolveModelRefAsync(cancellationToken).ConfigureAwait(false);
        return await ProposeForNodeAsync(workflow, node, modelRef, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public WorkflowDefinition AcceptProposal(
        WorkflowDefinition workflow,
        RealizationProposal proposal,
        RealizedNodeConfigEnvelope acceptedConfig)
    {
        var targetNode = workflow.Nodes.FirstOrDefault(candidate => candidate.Id == proposal.NodeId)
            ?? throw new ArgumentException(
                $"No node with id '{proposal.NodeId}' exists in this workflow.", nameof(proposal));

        var functionConfig    = NodeConfigSerializer.WriteEnvelope(acceptedConfig);
        var intrinsicProblems = NodeConfigSerializer.IntrinsicErrors(targetNode.NodeType, functionConfig);
        if (intrinsicProblems.Count > 0)
            throw new ArgumentException(
                "The accepted configuration is not valid: " + string.Join(" ", intrinsicProblems),
                nameof(acceptedConfig));

        // Only the one node changes: write its config and mark it configured; everything else is preserved.
        var updatedNodes = workflow.Nodes
            .Select(candidate => candidate.Id == targetNode.Id
                ? candidate with { FunctionConfig = functionConfig, IsConfigured = true }
                : candidate)
            .ToList();

        var workflowWithNode = workflow with { Nodes = updatedNodes };

        // Record the intent hash so a later change to this node's intent flags it as out-of-date (R6).
        var intentHash   = WorkflowIntentHasher.ComputeIntentHash(targetNode, workflowWithNode);
        var provenance   = new Dictionary<string, string>(workflow.Settings.RealizationProvenance)
        {
            [targetNode.Id] = intentHash,
        };

        return workflowWithNode with
        {
            Settings = workflow.Settings with { RealizationProvenance = provenance },
        };
    }

    // ── Per-node proposal dispatch ──────────────────────────────────────────────

    // Connector-category sets used to gate proposers before the LLM is called (T047).
    private static readonly IReadOnlySet<ConnectorType> MessagingConnectors =
        new HashSet<ConnectorType> { ConnectorType.Messaging };

    // spec-020: the work tracker is now the generic WorkTracker connector (provider chosen in the UI).
    private static readonly IReadOnlySet<ConnectorType> DataConnectors =
        new HashSet<ConnectorType> { ConnectorType.ServiceNow, ConnectorType.WorkTracker };

    /// <summary>
    /// Returns the connector category that must be configured before the LLM may be called for this
    /// node type, or null when the node needs no specific connector.
    /// </summary>
    private static IReadOnlySet<ConnectorType>? RequiredConnectorCategory(WorkflowNodeType nodeType) =>
        nodeType switch
        {
            WorkflowNodeType.FunctionNotify => MessagingConnectors,
            WorkflowNodeType.FunctionData   => DataConnectors,
            _                               => null,
        };

    /// <summary>
    /// Returns the configured connectors from the repository that belong to the given category.
    /// Only connectors with <c>IsConfigured = true</c> are considered available.
    /// </summary>
    private async Task<IReadOnlyList<ConnectorType>> GetConfiguredConnectorsInSetAsync(
        IReadOnlySet<ConnectorType> candidates, CancellationToken cancellationToken)
    {
        var all = await _connectorRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Where(config => config.IsConfigured && candidates.Contains(config.Type))
            .Select(config => config.Type)
            .ToList();
    }

    /// <summary>Routes a node to the proposer for its type and produces a reviewable proposal.</summary>
    private async Task<RealizationProposal> ProposeForNodeAsync(
        WorkflowDefinition workflow, WorkflowNode node, string modelRef, CancellationToken cancellationToken)
    {
        // T047: if this node needs a specific connector, verify at least one is configured before
        // calling the LLM. Returning Blocked immediately avoids a wasted LLM call and tells the
        // user honestly what they need to set up in Settings → Connectors.
        var requiredCategory = RequiredConnectorCategory(node.NodeType);
        if (requiredCategory is not null)
        {
            var configured = await GetConfiguredConnectorsInSetAsync(requiredCategory, cancellationToken)
                .ConfigureAwait(false);
            if (configured.Count == 0)
            {
                var connectorNames = string.Join(" or ", requiredCategory.Select(type => type.ToString()));
                return new RealizationProposal
                {
                    NodeId               = node.Id,
                    NodeType             = node.NodeType,
                    ProposedConfig       = null,
                    PlainLanguageSummary = $"Cannot configure \"{node.Label}\" — no connector is set up for this step.",
                    Status               = NodeRealizationStatus.Blocked,
                    BlockingReason       = $"This step needs a connector ({connectorNames}) that has not been "
                                         + "configured yet. Go to Settings → Connectors to add it.",
                };
            }
        }

        try
        {
            var (envelope, summary) = node.NodeType switch
            {
                WorkflowNodeType.Trigger        => await ProposeTriggerAsync(node, cancellationToken),
                WorkflowNodeType.AgenticReason  => await ProposeAgentAsync(workflow, node, modelRef, cancellationToken),
                WorkflowNodeType.FunctionRoute  => await ProposeRouteAsync(workflow, node, cancellationToken),
                WorkflowNodeType.FunctionTransform => await ProposeTransformAsync(node, cancellationToken),
                WorkflowNodeType.FunctionNotify => await ProposeNotifyAsync(node, cancellationToken),
                WorkflowNodeType.FunctionData   => await ProposeDataAsync(node, cancellationToken),
                WorkflowNodeType.HumanApproval  => await ProposeApprovalAsync(node, cancellationToken),
                _ => throw new NotSupportedException($"Node type {node.NodeType} cannot be realized."),
            };

            return BuildProposal(node, envelope, summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed proposal is surfaced honestly as NeedsInput rather than aborting the whole run.
            return new RealizationProposal
            {
                NodeId               = node.Id,
                NodeType             = node.NodeType,
                ProposedConfig       = null,
                PlainLanguageSummary = $"The assistant could not propose a setup for \"{node.Label}\".",
                Status               = NodeRealizationStatus.NeedsInput,
                BlockingReason       = ex.Message,
            };
        }
    }

    /// <summary>Wraps a produced envelope in a proposal, validating it intrinsically to set the status.</summary>
    private static RealizationProposal BuildProposal(
        WorkflowNode node, RealizedNodeConfigEnvelope envelope, string summary)
    {
        var functionConfig = NodeConfigSerializer.WriteEnvelope(envelope);
        var intrinsicErrors = NodeConfigSerializer.IntrinsicErrors(node.NodeType, functionConfig);

        return new RealizationProposal
        {
            NodeId               = node.Id,
            NodeType             = node.NodeType,
            ProposedConfig       = envelope,
            PlainLanguageSummary = summary,
            Status               = intrinsicErrors.Count == 0
                ? NodeRealizationStatus.Proposed
                : NodeRealizationStatus.NeedsInput,
            BlockingReason       = intrinsicErrors.Count == 0 ? null : string.Join(" ", intrinsicErrors),
        };
    }

    // ── Type-specific proposers ─────────────────────────────────────────────────

    /// <summary>Proposes the trigger's initial-data description and structured output shape.</summary>
    private async Task<(RealizedNodeConfigEnvelope, string)> ProposeTriggerAsync(
        WorkflowNode node, CancellationToken cancellationToken)
    {
        var dto = await _structuredService.GetStructuredAsync<TriggerProposalDto>(
            RealizationPrompts.System,
            RealizationPrompts.TriggerUser(node),
            "propose_trigger_config", "Describe what starts the workflow and the shape of its initial data.",
            RealizationSchemas.Trigger, cancellationToken).ConfigureAwait(false);

        var config = new TriggerNodeConfig
        {
            InitialDataDescription = dto.InitialDataDescription ?? string.Empty,
            OutputShape            = MapOutputShape(dto.OutputShape),
        };
        var summary = $"Starts when: {config.InitialDataDescription}. "
            + DescribeShape("Provides", config.OutputShape);
        return (BuildEnvelope(WorkflowNodeType.Trigger, config), summary);
    }

    /// <summary>Proposes the agentic step's instruction, model, output shape, and tool bindings.</summary>
    private async Task<(RealizedNodeConfigEnvelope, string)> ProposeAgentAsync(
        WorkflowDefinition workflow, WorkflowNode node, string modelRef, CancellationToken cancellationToken)
    {
        var dto = await _structuredService.GetStructuredAsync<AgentProposalDto>(
            RealizationPrompts.System,
            RealizationPrompts.AgentUser(workflow, node),
            "propose_agent_config", "Describe the AI step's instruction, structured output, and any tools it needs.",
            RealizationSchemas.Agent, cancellationToken).ConfigureAwait(false);

        var config = new AgentNodeConfig
        {
            Instruction  = dto.Instruction ?? string.Empty,
            ModelRef     = modelRef,
            OutputShape  = MapOutputShape(dto.OutputShape),
            ToolBindings = MapConnectors(dto.ToolBindings),
        };
        var summary = $"AI step: {config.Instruction} "
            + DescribeShape("Produces", config.OutputShape)
            + (config.ToolBindings.Count > 0 ? $" Uses: {string.Join(", ", config.ToolBindings)}." : string.Empty);
        return (BuildEnvelope(WorkflowNodeType.AgenticReason, config), summary);
    }

    /// <summary>Proposes the branch step's per-port conditions and a guaranteed default path.</summary>
    private async Task<(RealizedNodeConfigEnvelope, string)> ProposeRouteAsync(
        WorkflowDefinition workflow, WorkflowNode node, CancellationToken cancellationToken)
    {
        var dto = await _structuredService.GetStructuredAsync<RouteProposalDto>(
            RealizationPrompts.System,
            RealizationPrompts.RouteUser(workflow, node),
            "propose_route_config", "Describe one condition per outgoing path and the default path.",
            RealizationSchemas.Route, cancellationToken).ConfigureAwait(false);

        var conditions = (dto.Conditions ?? new List<RouteConditionDto>())
            .Select(condition => new RouteCondition(condition.OutputPortId ?? string.Empty, condition.Expression ?? string.Empty))
            .ToList();
        var config = new RouteNodeConfig
        {
            Conditions    = conditions,
            DefaultPortId = dto.DefaultPortId ?? node.OutputPorts.FirstOrDefault()?.Id ?? string.Empty,
        };
        var summary = $"Branches into {conditions.Count} path(s); default goes to \"{config.DefaultPortId}\".";
        return (BuildEnvelope(WorkflowNodeType.FunctionRoute, config), summary);
    }

    /// <summary>Proposes the transform step's input→output field mappings.</summary>
    private async Task<(RealizedNodeConfigEnvelope, string)> ProposeTransformAsync(
        WorkflowNode node, CancellationToken cancellationToken)
    {
        var dto = await _structuredService.GetStructuredAsync<TransformProposalDto>(
            RealizationPrompts.System,
            RealizationPrompts.TransformUser(node),
            "propose_transform_config", "Describe how upstream fields map to the downstream shape.",
            RealizationSchemas.Transform, cancellationToken).ConfigureAwait(false);

        var mappings = (dto.FieldMappings ?? new List<FieldMappingDto>())
            .Select(mapping => new FieldMapping(mapping.FromField ?? string.Empty, mapping.ToField ?? string.Empty))
            .ToList();
        var config  = new TransformNodeConfig { FieldMappings = mappings };
        var summary = $"Reshapes data with {mappings.Count} field mapping(s).";
        return (BuildEnvelope(WorkflowNodeType.FunctionTransform, config), summary);
    }

    /// <summary>Proposes the notification step's connector, recipient, and message template.</summary>
    private async Task<(RealizedNodeConfigEnvelope, string)> ProposeNotifyAsync(
        WorkflowNode node, CancellationToken cancellationToken)
    {
        var dto = await _structuredService.GetStructuredAsync<NotifyProposalDto>(
            RealizationPrompts.System,
            RealizationPrompts.NotifyUser(node),
            "propose_notify_config", "Describe the messaging connector, the recipient, and the message.",
            RealizationSchemas.Notify, cancellationToken).ConfigureAwait(false);

        var config = new NotifyNodeConfig
        {
            Connector       = ParseConnector(dto.Connector, ConnectorType.Messaging),
            RecipientMap    = dto.RecipientMap ?? string.Empty,
            MessageTemplate = dto.MessageTemplate ?? string.Empty,
        };
        var summary = $"Notifies via {config.Connector}: \"{config.MessageTemplate}\" to {config.RecipientMap}.";
        return (BuildEnvelope(WorkflowNodeType.FunctionNotify, config), summary);
    }

    /// <summary>Proposes the data step's connector, read/write operation, and data mappings.</summary>
    private async Task<(RealizedNodeConfigEnvelope, string)> ProposeDataAsync(
        WorkflowNode node, CancellationToken cancellationToken)
    {
        var dto = await _structuredService.GetStructuredAsync<DataProposalDto>(
            RealizationPrompts.System,
            RealizationPrompts.DataUser(node),
            "propose_data_config", "Describe the data connector, whether it reads or writes, and the mappings.",
            RealizationSchemas.Data, cancellationToken).ConfigureAwait(false);

        var operation = Enum.TryParse<DataOperation>(dto.Operation, ignoreCase: true, out var parsed)
            ? parsed : DataOperation.Read;
        var config = new DataNodeConfig
        {
            Connector = ParseConnector(dto.Connector, ConnectorType.ServiceNow),
            Operation = operation,
            InputMap  = dto.InputMap ?? string.Empty,
            OutputMap = dto.OutputMap ?? string.Empty,
        };
        var summary = $"{operation}s data via {config.Connector}.";
        return (BuildEnvelope(WorkflowNodeType.FunctionData, config), summary);
    }

    /// <summary>Proposes the human-approval step's approver, prompt, and decision options.</summary>
    private async Task<(RealizedNodeConfigEnvelope, string)> ProposeApprovalAsync(
        WorkflowNode node, CancellationToken cancellationToken)
    {
        var dto = await _structuredService.GetStructuredAsync<ApprovalProposalDto>(
            RealizationPrompts.System,
            RealizationPrompts.ApprovalUser(node),
            "propose_approval_config", "Describe who approves, what they see, and their decision options.",
            RealizationSchemas.Approval, cancellationToken).ConfigureAwait(false);

        var options = dto.DecisionOptions is { Count: > 0 }
            ? dto.DecisionOptions
            : new List<string> { "Approve", "Reject" };
        var config = new ApprovalNodeConfig
        {
            Approver        = dto.Approver ?? string.Empty,
            PromptShown     = dto.PromptShown ?? string.Empty,
            DecisionOptions = options,
        };
        var summary = $"Pauses for {config.Approver} to choose: {string.Join(" / ", options)}.";
        return (BuildEnvelope(WorkflowNodeType.HumanApproval, config), summary);
    }

    // ── Mapping & ordering helpers ──────────────────────────────────────────────

    /// <summary>Serializes a type-specific config into an envelope ready to attach to a proposal.</summary>
    private static RealizedNodeConfigEnvelope BuildEnvelope<TConfig>(WorkflowNodeType nodeType, TConfig config)
    {
        var json = NodeConfigSerializer.ToFunctionConfig(nodeType, config);
        return NodeConfigSerializer.ReadEnvelope(json)
            ?? throw new InvalidOperationException("Failed to build a configuration envelope.");
    }

    /// <summary>Maps the LLM's output-field DTOs to the typed <see cref="OutputField"/> shape.</summary>
    private static IReadOnlyList<OutputField> MapOutputShape(IReadOnlyList<OutputFieldDto>? fields)
    {
        if (fields is null || fields.Count == 0)
            return Array.Empty<OutputField>();

        return fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .Select(field => new OutputField(
                field.Name!,
                Enum.TryParse<OutputFieldKind>(field.Kind, ignoreCase: true, out var kind) ? kind : OutputFieldKind.Text,
                field.AllowedValues is { Count: > 0 } ? field.AllowedValues : null))
            .ToList();
    }

    /// <summary>Parses the LLM's connector-name strings into known <see cref="ConnectorType"/> values.</summary>
    private static IReadOnlyList<ConnectorType> MapConnectors(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
            return Array.Empty<ConnectorType>();

        return names
            .Select(name => Enum.TryParse<ConnectorType>(name, ignoreCase: true, out var type) ? type : (ConnectorType?)null)
            .Where(type => type.HasValue)
            .Select(type => type!.Value)
            .Distinct()
            .ToList();
    }

    /// <summary>Parses a single connector name, falling back to a sensible default for the node type.</summary>
    private static ConnectorType ParseConnector(string? name, ConnectorType fallback) =>
        Enum.TryParse<ConnectorType>(name, ignoreCase: true, out var type) ? type : fallback;

    /// <summary>Plain-language one-liner describing a structured output shape, or empty when none.</summary>
    private static string DescribeShape(string verb, IReadOnlyList<OutputField> shape) =>
        shape.Count == 0 ? string.Empty : $"{verb}: {string.Join(", ", shape.Select(field => field.Name))}.";

    /// <summary>Resolves the agent model from the LLM connector config, or the built-in default.</summary>
    private async Task<string> ResolveModelRefAsync(CancellationToken cancellationToken)
    {
        var llmConfig = await _connectorRepo.GetAsync(ConnectorType.LLM, cancellationToken).ConfigureAwait(false);
        if (llmConfig?.NonSecretConfig is not { } nonSecretJson)
            return DefaultModelRef;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(nonSecretJson);
            if (document.RootElement.TryGetProperty("modelName", out var modelProperty)
                && modelProperty.GetString() is { Length: > 0 } modelName)
                return modelName;
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed connector config falls back to the default model rather than failing realization.
        }

        return DefaultModelRef;
    }

    /// <summary>
    /// Orders nodes trigger-first by breadth-first traversal of the edges, then appends any nodes the
    /// traversal did not reach (in their original order) so every node is proposed exactly once.
    /// </summary>
    private static IReadOnlyList<WorkflowNode> OrderNodesForRealization(WorkflowDefinition workflow)
    {
        var nodesById  = workflow.Nodes.ToDictionary(node => node.Id);
        var downstream = workflow.Edges
            .GroupBy(edge => edge.SourceNodeId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetNodeId).ToList());

        var ordered = new List<WorkflowNode>();
        var visited = new HashSet<string>();
        var queue   = new Queue<string>();

        // Triggers are the natural entry points; if there are none, start from the first node.
        foreach (var trigger in workflow.Nodes.Where(node => node.NodeType == WorkflowNodeType.Trigger))
            queue.Enqueue(trigger.Id);
        if (queue.Count == 0 && workflow.Nodes.Count > 0)
            queue.Enqueue(workflow.Nodes[0].Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!visited.Add(currentId) || !nodesById.TryGetValue(currentId, out var currentNode))
                continue;
            ordered.Add(currentNode);
            if (downstream.TryGetValue(currentId, out var targets))
                foreach (var targetId in targets)
                    queue.Enqueue(targetId);
        }

        // Any node not reachable from a trigger is still realized, in its original position.
        foreach (var node in workflow.Nodes.Where(node => !visited.Contains(node.Id)))
            ordered.Add(node);

        return ordered;
    }

    // ── LLM output DTOs (bound from forced tool input; enum-like fields are strings) ─────────────

    private sealed record OutputFieldDto(string? Name, string? Kind, List<string>? AllowedValues);
    private sealed record TriggerProposalDto(string? InitialDataDescription, List<OutputFieldDto>? OutputShape);
    private sealed record AgentProposalDto(string? Instruction, List<OutputFieldDto>? OutputShape, List<string>? ToolBindings);
    private sealed record RouteConditionDto(string? OutputPortId, string? Expression);
    private sealed record RouteProposalDto(List<RouteConditionDto>? Conditions, string? DefaultPortId);
    private sealed record FieldMappingDto(string? FromField, string? ToField);
    private sealed record TransformProposalDto(List<FieldMappingDto>? FieldMappings);
    private sealed record NotifyProposalDto(string? Connector, string? RecipientMap, string? MessageTemplate);
    private sealed record DataProposalDto(string? Connector, string? Operation, string? InputMap, string? OutputMap);
    private sealed record ApprovalProposalDto(string? Approver, string? PromptShown, List<string>? DecisionOptions);
}
