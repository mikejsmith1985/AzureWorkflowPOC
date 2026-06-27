using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Verifies the orchestrator pushes the summary + gaps + portal link to the approval notifier when
/// a run pauses for review (T052) — the outbound half of the HITL loop.
/// </summary>
public class PhaseApprovalNotifierTests
{
    [Fact]
    public async Task OnPause_NotifierReceivesSummaryGapsAndPortalLink()
    {
        var validation = new PhaseValidationResult
        {
            Summary = "Summary for the reviewer.",
            Gaps = [new PhaseValidationGap { Label = "Risk", Description = "A risk to review." }],
        };
        var notifier = new FakePhaseApprovalNotifier();

        Func<IPhaseProgressSink, Kernel> kernelFactory = sink =>
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IArtifactReader>(
                new FakeArtifactReader([new PhaseArtifact { FileName = "spec.md", Content = "content" }]));
            builder.Services.AddSingleton<IStructuredCompletionService>(new FakeStructuredCompletionService(validation));
            builder.Services.AddSingleton<IBoardsClient>(new FakeBoardsClient());
            builder.Services.AddSingleton(sink);
            return builder.Build();
        };

        var orchestrator = new PhaseHandlerOrchestrator(
            kernelFactory, repository: null, approvalNotifier: notifier, portalBaseUrl: "http://localhost:5000");

        var runId = orchestrator.StartRun(new PhaseHandlerState
        {
            RunId = "run12345",
            FeatureKey = "001-feature",
            FeatureDirectory = "specs/001-feature",
            Phase = SpecKitPhase.Specify,
        });

        // Wait for the notifier to be invoked at pause time.
        for (var attempt = 0; attempt < 200 && notifier.Notifications.Count == 0; attempt++)
            await Task.Delay(10);

        Assert.Single(notifier.Notifications);
        var notification = notifier.Notifications[0];
        Assert.Equal(runId, notification.RunId);
        Assert.Equal(SpecKitPhase.Specify, notification.Phase);
        Assert.Equal("Summary for the reviewer.", notification.Summary);
        Assert.Single(notification.Gaps);
        Assert.Contains("run12345", notification.PortalUrl);
    }
}
