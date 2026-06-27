// Unit tests (T052): the Data and Transform steps consume their realized config. The pure mapping /
// describe helpers are tested directly — no kernel, no I/O (Article V).

using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Steps;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class RuntimeStepConfigTests
{
    // ── FunctionTransformStep.ApplyMappings ───────────────────────────────────────

    [Fact]
    public void Transform_applies_field_mappings_to_a_json_object()
    {
        var mappings = new[]
        {
            new FieldMapping("urgency", "priority"),
            new FieldMapping("summary", "title"),
        };
        const string input = """{ "urgency": "high", "summary": "DB down", "ignored": 1 }""";

        var output = FunctionTransformStep.ApplyMappings(input, mappings);

        // Only the mapped fields survive, renamed to their target names.
        Assert.NotNull(output);
        using var document = System.Text.Json.JsonDocument.Parse(output!);
        Assert.Equal("high", document.RootElement.GetProperty("priority").GetString());
        Assert.Equal("DB down", document.RootElement.GetProperty("title").GetString());
        Assert.False(document.RootElement.TryGetProperty("ignored", out _));
        Assert.False(document.RootElement.TryGetProperty("urgency", out _));
    }

    [Fact]
    public void Transform_passes_through_when_no_mappings()
    {
        const string input = """{ "a": 1 }""";
        Assert.Equal(input, FunctionTransformStep.ApplyMappings(input, Array.Empty<FieldMapping>()));
    }

    [Fact]
    public void Transform_passes_through_non_json_input_unchanged()
    {
        const string input = "just some text";
        var mappings = new[] { new FieldMapping("a", "b") };
        Assert.Equal(input, FunctionTransformStep.ApplyMappings(input, mappings));
    }

    [Fact]
    public void Transform_skips_a_mapping_whose_source_field_is_absent()
    {
        var mappings = new[] { new FieldMapping("missing", "target") };
        var output   = FunctionTransformStep.ApplyMappings("""{ "present": "x" }""", mappings);

        using var document = System.Text.Json.JsonDocument.Parse(output!);
        Assert.False(document.RootElement.TryGetProperty("target", out _));
    }

    // ── FunctionDataStep.DescribeOperation ────────────────────────────────────────

    [Fact]
    public void Data_read_surfaces_the_output_mapping_when_connector_is_ready()
    {
        var config = new DataNodeConfig
        {
            Connector = ConnectorType.ServiceNow,
            Operation = DataOperation.Read,
            InputMap  = "ticketId",
            OutputMap = "ticket record",
        };

        var result = FunctionDataStep.DescribeOperation(config, input: "ignored", isConnectorReady: true);

        Assert.Contains("Read from ServiceNow", result);
        Assert.Contains("ticket record", result);
    }

    [Fact]
    public void Data_write_confirms_the_payload_when_connector_is_ready()
    {
        var config = new DataNodeConfig
        {
            Connector = ConnectorType.AzureDevOps,
            Operation = DataOperation.Write,
            InputMap  = "work item",
            OutputMap = "id",
        };

        var result = FunctionDataStep.DescribeOperation(config, input: "PAYLOAD", isConnectorReady: true);

        Assert.Contains("Wrote to AzureDevOps", result);
        Assert.Contains("PAYLOAD", result);
    }

    [Fact]
    public void Data_reports_unavailable_connector_honestly()
    {
        var config = new DataNodeConfig
        {
            Connector = ConnectorType.ServiceNow,
            Operation = DataOperation.Read,
            InputMap  = "x",
            OutputMap = "y",
        };

        var result = FunctionDataStep.DescribeOperation(config, input: null, isConnectorReady: false);

        Assert.Contains("not set up", result);
        Assert.Contains("ServiceNow", result);
    }
}
