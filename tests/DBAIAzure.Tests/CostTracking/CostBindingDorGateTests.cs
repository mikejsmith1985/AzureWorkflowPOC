// spec-017 T010 (US1): the cost binding key is a Definition-of-Ready condition — a run without a valid one
// must fail the gate before any model call, so untracked AI spend can never reach the board.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Executors;
using DBAIAzure.Tests.Fakes;
using Xunit;

namespace DBAIAzure.Tests.CostTracking;

/// <summary>
/// Proves <see cref="PhaseValidationExecutor"/> refuses to validate a run whose cost binding key is missing or
/// malformed (FR-002). The refusal must be a terminal failed state, not an exception, and must happen without
/// calling the model — an unattributable run costs money nobody can account for.
/// </summary>
public sealed class CostBindingDorGateTests
{
    private const string ValidKey = "BIND-7K3QF2AB";

    private static PhaseHandlerState StateWithKey(string? bindingKey) => new()
    {
        RunId            = "run-0001",
        FeatureKey       = "021-feature",
        FeatureDirectory = "specs/021-feature",
        Phase            = SpecKitPhase.Specify,
        CostBindingKey   = bindingKey,
    };

    /// <summary>A minter that accepts only the one well-formed key above, mirroring the real branch-safe format.</summary>
    private sealed class StubBindingKeyMinter : IBindingKeyMinter
    {
        public string Mint() => ValidKey;
        public bool IsValid(string? candidate) => candidate == ValidKey;
    }

    private static (PhaseValidationExecutor Executor, FakeStructuredCompletionService Model) Build()
    {
        var model = new FakeStructuredCompletionService(new PhaseValidationResult
        {
            Summary = "ok",
            Gaps    = [],
        });
        return (new PhaseValidationExecutor(model, new StubBindingKeyMinter()), model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-binding-key")]
    public async Task InvalidBindingKey_FailsDefinitionOfReady(string? bindingKey)
    {
        var (executor, _) = Build();
        var context = new RecordingWorkflowContext();

        await executor.HandleAsync(StateWithKey(bindingKey), context, CancellationToken.None);

        // A blocked run is yielded as terminal output, never forwarded on to the approval gate.
        var blocked = Assert.IsType<PhaseHandlerState>(Assert.Single(context.YieldedOutputs));
        Assert.Equal(PhaseRunStatus.Failed, blocked.Status);
        Assert.Contains("binding key", blocked.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task InvalidBindingKey_DoesNotCallTheModel()
    {
        // The gate must come first: an unattributable run must not spend money proving it is unattributable.
        var (executor, model) = Build();

        await executor.HandleAsync(StateWithKey(null), new RecordingWorkflowContext(), CancellationToken.None);

        Assert.Null(model.LastUserMessage);
    }

    [Fact]
    public async Task ValidBindingKey_PassesTheGateAndForwardsForApproval()
    {
        var (executor, model) = Build();
        var context = new RecordingWorkflowContext();

        await executor.HandleAsync(StateWithKey(ValidKey), context, CancellationToken.None);

        var validated = Assert.IsType<PhaseHandlerState>(Assert.Single(context.SentMessages));
        Assert.Equal(PhaseRunStatus.Validated, validated.Status);
        Assert.Empty(context.YieldedOutputs);
        Assert.NotNull(model.LastUserMessage);
    }

    [Fact]
    public async Task NoMinterConfigured_LeavesTheGateOpen()
    {
        // The minter is optional: a deployment without cost tracking must still be able to validate.
        var model    = new FakeStructuredCompletionService(new PhaseValidationResult { Summary = "ok", Gaps = [] });
        var executor = new PhaseValidationExecutor(model, bindingKeyMinter: null);
        var context  = new RecordingWorkflowContext();

        await executor.HandleAsync(StateWithKey(null), context, CancellationToken.None);

        Assert.Single(context.SentMessages);
        Assert.Empty(context.YieldedOutputs);
    }
}
