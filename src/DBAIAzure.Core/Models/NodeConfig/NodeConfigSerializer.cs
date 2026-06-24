// Serializes realized per-node configuration to/from the node's FunctionConfig blob and validates it.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The single place that turns a type-specific realized config record into the JSON stored in
/// <see cref="WorkflowNode.FunctionConfig"/> (wrapped in a <see cref="RealizedNodeConfigEnvelope"/>),
/// reads it back, and validates the intrinsic correctness of a node's realized config for its type.
/// Centralising this keeps the runtime steps, the realization service, and the readiness service in
/// agreement on the on-disk shape.
/// </summary>
public static class NodeConfigSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Wraps a type-specific config record in an envelope and serializes it for FunctionConfig.</summary>
    public static string ToFunctionConfig<TConfig>(WorkflowNodeType nodeType, TConfig config)
    {
        var envelope = new RealizedNodeConfigEnvelope
        {
            NodeType   = nodeType,
            ConfigJson = JsonSerializer.Serialize(config, JsonOptions),
        };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    /// <summary>Serializes an already-built envelope to the JSON string stored in FunctionConfig.</summary>
    public static string WriteEnvelope(RealizedNodeConfigEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, JsonOptions);

    /// <summary>Reads the envelope from a FunctionConfig blob, or null if absent/unparseable.</summary>
    public static RealizedNodeConfigEnvelope? ReadEnvelope(string? functionConfig)
    {
        if (string.IsNullOrWhiteSpace(functionConfig))
            return null;
        try
        {
            return JsonSerializer.Deserialize<RealizedNodeConfigEnvelope>(functionConfig, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads and deserializes the type-specific config from a FunctionConfig blob, or null.</summary>
    public static TConfig? ReadConfig<TConfig>(string? functionConfig) where TConfig : class
    {
        var envelope = ReadEnvelope(functionConfig);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.ConfigJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TConfig>(envelope.ConfigJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates the intrinsic correctness of a node's realized config for its type (VAL-005): the
    /// blob deserializes to the right shape and its required fields are present and non-empty. Returns
    /// plain-language problems, or an empty list when the config is intrinsically valid. Cross-node
    /// rules (e.g. one route condition per edge) are checked by the validator/readiness with graph access.
    /// </summary>
    public static IReadOnlyList<string> IntrinsicErrors(WorkflowNodeType nodeType, string? functionConfig)
    {
        var problems = new List<string>();
        var envelope = ReadEnvelope(functionConfig);
        if (envelope is null)
        {
            problems.Add("This step has not been set up for production yet.");
            return problems;
        }

        switch (nodeType)
        {
            case WorkflowNodeType.AgenticReason:
                var agent = ReadConfig<AgentNodeConfig>(functionConfig);
                if (agent is null) problems.Add("The AI step's configuration could not be read.");
                else
                {
                    if (string.IsNullOrWhiteSpace(agent.Instruction)) problems.Add("The AI step is missing its instruction.");
                    if (string.IsNullOrWhiteSpace(agent.ModelRef))     problems.Add("The AI step is missing the model it should use.");
                }
                break;

            case WorkflowNodeType.FunctionNotify:
                var notify = ReadConfig<NotifyNodeConfig>(functionConfig);
                if (notify is null) problems.Add("The notification step's configuration could not be read.");
                else
                {
                    if (string.IsNullOrWhiteSpace(notify.RecipientMap))    problems.Add("The notification step is missing a recipient.");
                    if (string.IsNullOrWhiteSpace(notify.MessageTemplate)) problems.Add("The notification step is missing a message.");
                }
                break;

            case WorkflowNodeType.FunctionData:
                var data = ReadConfig<DataNodeConfig>(functionConfig);
                if (data is null) problems.Add("The data step's configuration could not be read.");
                else if (string.IsNullOrWhiteSpace(data.InputMap) && string.IsNullOrWhiteSpace(data.OutputMap))
                    problems.Add("The data step is missing its input/output mapping.");
                break;

            case WorkflowNodeType.FunctionRoute:
                var route = ReadConfig<RouteNodeConfig>(functionConfig);
                if (route is null) problems.Add("The branch step's configuration could not be read.");
                else
                {
                    if (route.Conditions.Count == 0)              problems.Add("The branch step has no conditions.");
                    if (string.IsNullOrWhiteSpace(route.DefaultPortId)) problems.Add("The branch step has no default path.");
                }
                break;

            case WorkflowNodeType.FunctionTransform:
                var transform = ReadConfig<TransformNodeConfig>(functionConfig);
                if (transform is null || transform.FieldMappings.Count == 0)
                    problems.Add("The transform step has no field mappings.");
                break;

            case WorkflowNodeType.HumanApproval:
                var approval = ReadConfig<ApprovalNodeConfig>(functionConfig);
                if (approval is null) problems.Add("The approval step's configuration could not be read.");
                else
                {
                    if (string.IsNullOrWhiteSpace(approval.PromptShown)) problems.Add("The approval step is missing what to show the approver.");
                    if (approval.DecisionOptions.Count < 2)             problems.Add("The approval step needs at least two decision options.");
                }
                break;

            case WorkflowNodeType.Trigger:
                var trigger = ReadConfig<TriggerNodeConfig>(functionConfig);
                if (trigger is null || string.IsNullOrWhiteSpace(trigger.InitialDataDescription))
                    problems.Add("The trigger is missing a description of what starts the workflow.");
                break;
        }

        return problems;
    }
}
