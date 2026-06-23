// Unit tests (T019): every per-type realized config round-trips through the node's FunctionConfig blob,
// and intrinsic validation flags missing required fields. Pure domain — no I/O, no mocks.

using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class NodeConfigSerializationTests
{
    [Fact]
    public void Agent_config_round_trips_through_function_config()
    {
        var original = new AgentNodeConfig
        {
            Instruction  = "Classify the ticket urgency.",
            ModelRef     = "claude-test-model",
            OutputShape  = new[] { new OutputField("urgency", OutputFieldKind.Enum, new[] { "low", "high" }) },
            ToolBindings = new[] { ConnectorType.ServiceNow },
        };

        var json    = NodeConfigSerializer.ToFunctionConfig(WorkflowNodeType.AgenticReason, original);
        var restored = NodeConfigSerializer.ReadConfig<AgentNodeConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Instruction, restored!.Instruction);
        Assert.Equal(original.ModelRef, restored.ModelRef);
        Assert.Single(restored.OutputShape);
        Assert.Equal("urgency", restored.OutputShape[0].Name);
        Assert.Equal(OutputFieldKind.Enum, restored.OutputShape[0].Kind);
        Assert.Equal(ConnectorType.ServiceNow, Assert.Single(restored.ToolBindings));
    }

    [Fact]
    public void Notify_config_round_trips_and_preserves_connector_enum()
    {
        var original = new NotifyNodeConfig
        {
            Connector       = ConnectorType.Teams,
            RecipientMap    = "on-call channel",
            MessageTemplate = "Ticket {id} is urgent.",
        };

        var json     = NodeConfigSerializer.ToFunctionConfig(WorkflowNodeType.FunctionNotify, original);
        var restored = NodeConfigSerializer.ReadConfig<NotifyNodeConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal(ConnectorType.Teams, restored!.Connector);
        Assert.Equal(original.RecipientMap, restored.RecipientMap);
        Assert.Equal(original.MessageTemplate, restored.MessageTemplate);
    }

    [Fact]
    public void Envelope_carries_node_type_discriminator_and_schema_version()
    {
        var config   = new TriggerNodeConfig { InitialDataDescription = "A new ServiceNow ticket is created." };
        var json     = NodeConfigSerializer.ToFunctionConfig(WorkflowNodeType.Trigger, config);
        var envelope = NodeConfigSerializer.ReadEnvelope(json);

        Assert.NotNull(envelope);
        Assert.Equal(WorkflowNodeType.Trigger, envelope!.NodeType);
        Assert.Equal(1, envelope.SchemaVersion);
    }

    [Fact]
    public void Intrinsic_errors_flag_missing_required_agent_fields()
    {
        // An agent with no instruction and no model is intrinsically invalid.
        var invalid = new AgentNodeConfig { Instruction = "", ModelRef = "" };
        var json    = NodeConfigSerializer.ToFunctionConfig(WorkflowNodeType.AgenticReason, invalid);

        var problems = NodeConfigSerializer.IntrinsicErrors(WorkflowNodeType.AgenticReason, json);

        Assert.Contains(problems, problem => problem.Contains("instruction"));
        Assert.Contains(problems, problem => problem.Contains("model"));
    }

    [Fact]
    public void Intrinsic_errors_flag_unrealized_node()
    {
        var problems = NodeConfigSerializer.IntrinsicErrors(WorkflowNodeType.FunctionNotify, functionConfig: null);
        Assert.NotEmpty(problems);
    }

    [Fact]
    public void Valid_config_has_no_intrinsic_errors()
    {
        var config = new NotifyNodeConfig
        {
            Connector       = ConnectorType.Teams,
            RecipientMap    = "team",
            MessageTemplate = "hello",
        };
        var json = NodeConfigSerializer.ToFunctionConfig(WorkflowNodeType.FunctionNotify, config);

        Assert.Empty(NodeConfigSerializer.IntrinsicErrors(WorkflowNodeType.FunctionNotify, json));
    }
}
