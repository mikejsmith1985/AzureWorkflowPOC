// Unit tests for DorNodeSettingsConfig — proves a node's DoR settings round-trip through its FunctionConfig blob
// and coexist with the node's references without either clobbering the other. Pure, <10ms.
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorNodeSettingsConfigTests
{
    [Fact]
    public void Read_Null_ReturnsNull() => Assert.Null(DorNodeSettingsConfig.Read(null));

    [Fact]
    public void Read_MalformedJson_ReturnsNull() => Assert.Null(DorNodeSettingsConfig.Read("{ not json"));

    [Fact]
    public void Read_ConfigWithoutDorObject_ReturnsNull() =>
        Assert.Null(DorNodeSettingsConfig.Read("{\"initialDataDescription\":\"x\"}"));

    [Fact]
    public void WriteThenRead_RoundTripsRoleAndValues()
    {
        var settings = new DorNodeSettings
        {
            Role = DorNodeRole.Trigger,
            ProjectKeys = new[] { "SBRO", "OPS" },
            IssueTypes = new[] { "Story" },
            DryRun = true,
        };

        var readBack = DorNodeSettingsConfig.Read(DorNodeSettingsConfig.Write(null, settings))!;

        Assert.Equal(DorNodeRole.Trigger, readBack.Role);
        Assert.Equal(new[] { "SBRO", "OPS" }, readBack.ProjectKeys);
        Assert.Equal(new[] { "Story" }, readBack.IssueTypes);
        Assert.True(readBack.DryRun);
        // Unset fields stay unset so the assembler can fall back to the connector configuration.
        Assert.Null(readBack.ReadyTransitionId);
        Assert.Null(readBack.MaxIterations);
    }

    [Fact]
    public void Write_PreservesOtherKeys_IncludingReferences()
    {
        // A node carries both its references and its settings; writing one must not drop the other.
        var withReference = NodeReferenceConfig.Write("{\"initialDataDescription\":\"ticket\"}", new[]
        {
            new NodeReference { Type = NodeReferenceType.Document, Name = "DoR", Value = "# doc" },
        });

        var merged = DorNodeSettingsConfig.Write(
            withReference, new DorNodeSettings { Role = DorNodeRole.Review, Temperature = 0.2 });

        Assert.Contains("initialDataDescription", merged);
        Assert.Single(NodeReferenceConfig.Read(merged));
        Assert.Equal(DorNodeRole.Review, DorNodeSettingsConfig.Read(merged)!.Role);
    }

    [Fact]
    public void ReferencesWrite_PreservesExistingSettings()
    {
        // The inverse direction: editing references must not drop the node's DoR settings.
        var withSettings = DorNodeSettingsConfig.Write(
            null, new DorNodeSettings { Role = DorNodeRole.Escalate, ManualLabel = "manual" });

        var merged = NodeReferenceConfig.Write(withSettings, new[]
        {
            new NodeReference { Type = NodeReferenceType.Url, Name = "Runbook", Value = "https://r" },
        });

        Assert.Equal(DorNodeRole.Escalate, DorNodeSettingsConfig.Read(merged)!.Role);
        Assert.Equal("manual", DorNodeSettingsConfig.Read(merged)!.ManualLabel);
        Assert.Single(NodeReferenceConfig.Read(merged));
    }

    [Fact]
    public void Write_Null_RemovesSettings_ButKeepsOtherKeys()
    {
        var withSettings = DorNodeSettingsConfig.Write(
            "{\"initialDataDescription\":\"x\"}", new DorNodeSettings { Role = DorNodeRole.Audit });

        var cleared = DorNodeSettingsConfig.Write(withSettings, null);

        Assert.Null(DorNodeSettingsConfig.Read(cleared));
        Assert.Contains("initialDataDescription", cleared);
    }

    [Fact]
    public void ReadRole_ReturnsNone_WhenNoSettings() =>
        Assert.Equal(DorNodeRole.None, DorNodeSettingsConfig.ReadRole("{}"));

    [Fact]
    public void ReadRole_ReturnsStoredRole()
    {
        var config = DorNodeSettingsConfig.Write(null, new DorNodeSettings { Role = DorNodeRole.Resolve });
        Assert.Equal(DorNodeRole.Resolve, DorNodeSettingsConfig.ReadRole(config));
    }
}
