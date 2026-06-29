using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Processes.Steps;
using DBAIAzure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Tests for PhaseValidationStep: the structured (fake) chat service result is bound onto the run
/// state and surfaced for review, and the artifact content is sent to the model. Exercised through
/// the real SK process with a fake IStructuredCompletionService — no LLM call is made.
/// </summary>
public class PhaseValidationStepTests
{
    [Fact]
    public void ValidationStep_DeclaresToolMatchingTheContractSchema()
    {
        // Guard the tool name against silent drift from contracts/validation-tool-schema.json.
        Assert.Equal("report_phase_validation", PhaseValidationStep.ToolName);
        Assert.Contains("summary", PhaseValidationStep.InputSchemaJson);
        Assert.Contains("gaps", PhaseValidationStep.InputSchemaJson);
    }

    [Fact]
    public async Task ValidationStep_BindsStructuredResult_AndSendsArtifacts()
    {
        var validation = new PhaseValidationResult
        {
            Summary = "Bound summary.",
            Gaps = [new PhaseValidationGap { Label = "G", Description = "D" }],
        };
        var fakeChat = new FakeStructuredCompletionService(validation);
        var boards = new FakeBoardsClient();

        Func<IPhaseProgressSink, Kernel> factory = sink =>
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IArtifactReader>(
                new FakeArtifactReader([new PhaseArtifact { FileName = "spec.md", Content = "UNIQUE_ARTIFACT_MARKER" }]));
            builder.Services.AddSingleton<IStructuredCompletionService>(fakeChat);
            builder.Services.AddSingleton<IBoardsClient>(boards);
            builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapter>(WorkTrackerAdapters.AdoAdapterFor(boards));
            builder.Services.AddSingleton(sink);
            return builder.Build();
        };

        var orchestrator = new PhaseHandlerOrchestrator(factory);
        var runId = orchestrator.StartRun(new PhaseHandlerState
        {
            RunId = Guid.NewGuid().ToString("N")[..8],
            FeatureKey = "001-feature",
            FeatureDirectory = "specs/001-feature",
            Phase = SpecKitPhase.Specify,
        });

        for (var attempt = 0; attempt < 200 && !orchestrator.GetRun(runId)!.IsAwaitingApproval; attempt++)
            await Task.Delay(10);

        var run = orchestrator.GetRun(runId)!;
        Assert.NotNull(run.State.Validation);
        Assert.Equal("Bound summary.", run.State.Validation!.Summary);
        Assert.Single(run.State.Validation.Gaps);
        Assert.NotNull(fakeChat.LastUserMessage);
        Assert.Contains("UNIQUE_ARTIFACT_MARKER", fakeChat.LastUserMessage);
    }
}
