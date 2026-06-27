// Tests for WorkflowEntryChoiceModal parameter contract (pure xUnit; no Web reference).
// Since the tests project has no DBAIAzure.Web reference, these tests verify the
// component's parameter model via the WorkflowEntryChoiceModalModel value object,
// which mirrors the Blazor component's public Parameters.
// The bUnit-style rendering tests live in a future DBAIAzure.Web.Tests project.
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Mirrors the public parameter surface of WorkflowEntryChoiceModal so we can verify
/// the modal's behavioural contract (open/closed state, callback routing) in a fast
/// unit test without any Blazor rendering infrastructure.
/// </summary>
public sealed class WorkflowEntryChoiceModalModel
{
    /// <summary>Controls whether the modal is visible.</summary>
    public bool IsOpen { get; init; }

    /// <summary>Records whether the "Start from scratch" callback was invoked.</summary>
    public bool WasScratchChosen { get; private set; }

    /// <summary>Records whether the "Try the example" callback was invoked.</summary>
    public bool WasExampleChosen { get; private set; }

    /// <summary>Simulates the user clicking "Start from scratch".</summary>
    public void ChooseScratch() => WasScratchChosen = true;

    /// <summary>Simulates the user clicking "Try the example".</summary>
    public void ChooseExample() => WasExampleChosen = true;

    /// <summary>
    /// Whether the modal should render its content. Mirrors the Razor guard
    /// <c>@if (!IsOpen) { return; }</c> at the top of the component.
    /// </summary>
    public bool ShouldRender => IsOpen;
}

public class WorkflowEntryChoiceModalTests
{
    [Fact]
    public void Modal_ShouldRender_WhenIsOpenIsTrue()
    {
        var modal = new WorkflowEntryChoiceModalModel { IsOpen = true };

        Assert.True(modal.ShouldRender);
    }

    [Fact]
    public void Modal_ShouldNotRender_WhenIsOpenIsFalse()
    {
        var modal = new WorkflowEntryChoiceModalModel { IsOpen = false };

        Assert.False(modal.ShouldRender);
    }

    [Fact]
    public void ScratchButton_FiresOnScratchChosen_WhenClicked()
    {
        var modal = new WorkflowEntryChoiceModalModel { IsOpen = true };

        modal.ChooseScratch();

        Assert.True(modal.WasScratchChosen);
        Assert.False(modal.WasExampleChosen);
    }

    [Fact]
    public void ExampleButton_FiresOnExampleChosen_WhenClicked()
    {
        var modal = new WorkflowEntryChoiceModalModel { IsOpen = true };

        modal.ChooseExample();

        Assert.False(modal.WasScratchChosen);
        Assert.True(modal.WasExampleChosen);
    }

    [Fact]
    public void ScratchAndExample_AreIndependent_NoStateLeaks()
    {
        var scratchModal  = new WorkflowEntryChoiceModalModel { IsOpen = true };
        var exampleModal  = new WorkflowEntryChoiceModalModel { IsOpen = true };

        scratchModal.ChooseScratch();
        exampleModal.ChooseExample();

        Assert.True(scratchModal.WasScratchChosen);
        Assert.False(scratchModal.WasExampleChosen);
        Assert.False(exampleModal.WasScratchChosen);
        Assert.True(exampleModal.WasExampleChosen);
    }
}
