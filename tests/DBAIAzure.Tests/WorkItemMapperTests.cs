// Tests for the central phase→work-item-type mapping (PhaseWorkItemMap), the testable core of the
// Specify→Epic / Plan→Task / Implement→Bug rule set (FR-007).
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Verifies <see cref="PhaseWorkItemMap"/> — the single source of truth for which Azure DevOps work
/// item type each phase produces (FR-007). The title/description derivation that consumes this map
/// lives in <c>WorkItemMapper</c> (DBAIAzure.Web, deliberately not referenced here to keep the unit
/// test project free of Blazor) and in <c>CreateWorkItemStep</c>; the latter's output is verified
/// end-to-end through the real SK process in <see cref="CreateWorkItemStepTests"/>.
/// </summary>
public class WorkItemMapperTests
{
    [Theory]
    [InlineData(SpecKitPhase.Specify, "Epic")]
    [InlineData(SpecKitPhase.Plan, "Task")]
    [InlineData(SpecKitPhase.Implement, "Bug")]
    public void ToWorkItemType_MapsEachSupportedPhase(SpecKitPhase phase, string expectedType)
    {
        Assert.Equal(expectedType, PhaseWorkItemMap.ToWorkItemType(phase));
    }

    [Fact]
    public void ToWorkItemType_Unsupported_IsNull()
    {
        Assert.Null(PhaseWorkItemMap.ToWorkItemType(SpecKitPhase.Unsupported));
    }

    [Theory]
    [InlineData("specify", SpecKitPhase.Specify)]
    [InlineData("Plan", SpecKitPhase.Plan)]
    [InlineData("IMPLEMENT", SpecKitPhase.Implement)]
    [InlineData("deploy", SpecKitPhase.Unsupported)]
    [InlineData("", SpecKitPhase.Unsupported)]
    [InlineData(null, SpecKitPhase.Unsupported)]
    public void Parse_IsCaseInsensitive_AndUnknownIsUnsupported(string? rawPhase, SpecKitPhase expected)
    {
        Assert.Equal(expected, PhaseWorkItemMap.Parse(rawPhase));
    }

    [Fact]
    public void TypeConstants_MatchAgileProcessNames()
    {
        Assert.Equal("Epic", PhaseWorkItemMap.EpicType);
        Assert.Equal("Task", PhaseWorkItemMap.TaskType);
        Assert.Equal("Bug", PhaseWorkItemMap.BugType);
    }
}
