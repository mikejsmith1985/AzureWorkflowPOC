using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Coverage for the Spec Kit webhook surface. The controller itself lives in DBAIAzure.Web, which the
/// test project does not reference (to avoid pulling in Blazor server infrastructure) — full HTTP-level
/// tests belong in a WebApplicationFactory integration project, mirroring the existing
/// <see cref="WebhookControllerTests"/> convention. Here we cover the decision logic the controller
/// delegates to: phase parsing (drives 202 vs Unsupported recording) and the orchestrator's
/// approval-result mapping (drives 200 vs 404/409).
/// </summary>
public class SpecKitWebhookControllerTests
{
    [Theory]
    [InlineData("specify", SpecKitPhase.Specify)]
    [InlineData("PLAN", SpecKitPhase.Plan)]
    [InlineData("Implement", SpecKitPhase.Implement)]
    [InlineData("deploy", SpecKitPhase.Unsupported)]
    [InlineData(null, SpecKitPhase.Unsupported)]
    public void PhaseParsing_IsCaseInsensitive_AndDefaultsToUnsupported(string? raw, SpecKitPhase expected)
    {
        Assert.Equal(expected, PhaseWorkItemMap.Parse(raw));
    }

    [Fact]
    public void SubmitApproval_ForUnknownRun_MapsToNotAwaiting_For404()
    {
        var orchestrator = BuildOrchestrator();

        var result = orchestrator.SubmitApproval("missing", new ApprovalDecision { IsApproved = true });

        Assert.Equal(PhaseHandlerOrchestrator.ApprovalResult.NotAwaiting, result);
    }

    [Fact]
    public async Task SubmitApproval_SecondTime_MapsToNotAwaiting_For409()
    {
        var orchestrator = BuildOrchestrator();
        var runId = orchestrator.StartRun(new PhaseHandlerState
        {
            RunId = Guid.NewGuid().ToString("N")[..8],
            FeatureKey = "001-feature",
            FeatureDirectory = "specs/001-feature",
            Phase = SpecKitPhase.Specify,
        });

        for (var attempt = 0; attempt < 200 && !orchestrator.GetRun(runId)!.IsAwaitingApproval; attempt++)
            await Task.Delay(10);

        var first = orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        var second = orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });

        Assert.Equal(PhaseHandlerOrchestrator.ApprovalResult.Applied, first);
        Assert.Equal(PhaseHandlerOrchestrator.ApprovalResult.NotAwaiting, second);
    }

    private static PhaseHandlerOrchestrator BuildOrchestrator()
    {
        var validation = new PhaseValidationResult { Summary = "s", Gaps = [] };
        var repo = new FakePhaseRunRepository();
        var writerDeps = new PhaseWorkItemWriterDeps(
            Tracker: WorkTrackerAdapters.AdoAdapterFor(new FakeBoardsClient()), Repository: repo);
        return new PhaseHandlerOrchestrator(
            PhaseValidationChat.Returning(validation), new FakeArtifactReader([new PhaseArtifact { FileName = "spec.md", Content = "x" }]), writerDeps, repo);
    }
}
