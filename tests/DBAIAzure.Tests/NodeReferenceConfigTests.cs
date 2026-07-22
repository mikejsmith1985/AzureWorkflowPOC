// Unit tests for NodeReferenceConfig — proves the references array round-trips through a node's
// FunctionConfig blob without disturbing other keys, and tolerates malformed input. Pure, <10ms.
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class NodeReferenceConfigTests
{
    [Fact]
    public void Read_Null_ReturnsEmpty() =>
        Assert.Empty(NodeReferenceConfig.Read(null));

    [Fact]
    public void Read_ConfigWithoutReferences_ReturnsEmpty() =>
        Assert.Empty(NodeReferenceConfig.Read("{\"initialDataDescription\":\"hello\"}"));

    [Fact]
    public void Read_MalformedJson_ReturnsEmpty() =>
        Assert.Empty(NodeReferenceConfig.Read("{ not json"));

    [Fact]
    public void Read_ParsesTypedReferences_CaseInsensitiveType()
    {
        const string json = """
            {"references":[
              {"type":"DOCUMENT","name":"DoR","value":"# Definition of Ready"},
              {"type":"url","name":"Runbook","value":"https://wiki/runbook"}
            ]}
            """;

        var references = NodeReferenceConfig.Read(json);

        Assert.Equal(2, references.Count);
        Assert.Equal(NodeReferenceType.Document, references[0].Type);
        Assert.Equal("DoR", references[0].Name);
        Assert.Equal("# Definition of Ready", references[0].Value);
        Assert.Equal(NodeReferenceType.Url, references[1].Type);
        Assert.Equal("https://wiki/runbook", references[1].Value);
    }

    [Fact]
    public void Read_SkipsReferencesWithoutName()
    {
        const string json = """{"references":[{"type":"url","value":"https://x"}]}""";
        Assert.Empty(NodeReferenceConfig.Read(json));
    }

    [Fact]
    public void Write_AddsReferences_PreservesOtherKeys()
    {
        const string existing = "{\"initialDataDescription\":\"ticket text\"}";
        var references = new[]
        {
            new NodeReference { Type = NodeReferenceType.Document, Name = "DoR", Value = "# DoR" },
        };

        var merged = NodeReferenceConfig.Write(existing, references);

        // The node's own type-specific key survives the merge...
        Assert.Contains("initialDataDescription", merged);
        Assert.Contains("ticket text", merged);
        // ...and the reference round-trips back out.
        var readBack = NodeReferenceConfig.Read(merged);
        Assert.Single(readBack);
        Assert.Equal("DoR", readBack[0].Name);
    }

    [Fact]
    public void Write_EmptyList_RemovesReferencesKey_PreservesOtherKeys()
    {
        const string existing =
            "{\"initialDataDescription\":\"x\",\"references\":[{\"type\":\"url\",\"name\":\"n\",\"value\":\"v\"}]}";

        var merged = NodeReferenceConfig.Write(existing, Array.Empty<NodeReference>());

        Assert.DoesNotContain("references", merged);
        Assert.Contains("initialDataDescription", merged);
    }

    [Fact]
    public void Write_NullConfig_CreatesObjectWithReferences()
    {
        var references = new[]
        {
            new NodeReference { Type = NodeReferenceType.Dashboard, Name = "Ops", Value = "https://dash" },
        };

        var readBack = NodeReferenceConfig.Read(NodeReferenceConfig.Write(null, references));

        Assert.Single(readBack);
        Assert.Equal(NodeReferenceType.Dashboard, readBack[0].Type);
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllTypes()
    {
        var references = new[]
        {
            new NodeReference { Type = NodeReferenceType.Document, Name = "Doc", Value = "text" },
            new NodeReference { Type = NodeReferenceType.Url, Name = "Url", Value = "https://u" },
            new NodeReference { Type = NodeReferenceType.Dashboard, Name = "Dash", Value = "https://d" },
            new NodeReference { Type = NodeReferenceType.Binary, Name = "Blob", Value = "blob://b" },
        };

        var readBack = NodeReferenceConfig.Read(NodeReferenceConfig.Write(null, references));

        Assert.Equal(4, readBack.Count);
        Assert.Equal(references.Select(reference => reference.Type), readBack.Select(reference => reference.Type));
    }
}
