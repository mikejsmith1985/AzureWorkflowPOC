// Unit tests for AdoTelemetryPreflightStep SK process step (spec-009, T031).
// KernelProcessStepContext is sealed in SK 1.77 — tests verify state changes and service call counts
// rather than event emission. Event routing is covered by integration/E2E tests.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Processes.Steps;
using Microsoft.SemanticKernel;
using Xunit;

#pragma warning disable SKEXP0080

namespace DBAIAzure.Tests.Steps;

/// <summary>
/// Verifies that the preflight step transitions its state correctly after a service call,
/// and that it respects cancellation before invoking the service.
/// </summary>
public sealed class AdoTelemetryPreflightStepTests
{
    // ── T031: Service returns BootstrapManifest → step marks IsComplete=true ─────

    [Fact]
    public async Task RunPreflightAsync_WhenServiceSucceeds_StepStateIsComplete()
    {
        var manifest = BuildBootstrapManifest();
        var service = new AlwaysSucceedsPreflightService(manifest);

        var step = new AdoTelemetryPreflightStep();
        await ActivateStep(step, new AdoPreflightStepState());

        // null! context is safe — the step guards with `if (context is not null)` before emitting.
        await step.RunPreflightAsync(null!, service, overrideConfig: null, CancellationToken.None);

        var state = step.GetCurrentState();
        Assert.True(state.IsComplete);
        Assert.Equal(PreflightMode.Bootstrap, state.Mode);
    }

    // ── T031: Service returns IsSuccess=false → step does not mark IsComplete ────

    [Fact]
    public async Task RunPreflightAsync_WhenServiceFails_StepStateNotComplete()
    {
        var service = new AlwaysFailsPreflightService("ADO org URL is not configured.");

        var step = new AdoTelemetryPreflightStep();
        await ActivateStep(step, new AdoPreflightStepState());

        await step.RunPreflightAsync(null!, service, overrideConfig: null, CancellationToken.None);

        var state = step.GetCurrentState();
        Assert.False(state.IsComplete);
        Assert.Null(state.Mode);
    }

    // ── T031: CancellationToken already cancelled → service not called ───────────

    [Fact]
    public async Task RunPreflightAsync_WhenTokenAlreadyCancelled_DoesNotCallService()
    {
        var service = new TrackingPreflightService();

        var step = new AdoTelemetryPreflightStep();
        await ActivateStep(step, new AdoPreflightStepState());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await step.RunPreflightAsync(null!, service, overrideConfig: null, cts.Token);

        Assert.Equal(0, service.CallCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static Task ActivateStep(AdoTelemetryPreflightStep step, AdoPreflightStepState state) =>
        step.ActivateAsync(new KernelProcessStepState<AdoPreflightStepState>("test-step", "test-step-id")
        {
            State = state,
        }).AsTask();

    private static BootstrapManifest BuildBootstrapManifest() => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        OrgUrl = "https://dev.azure.com/test",
        Project = "TestProject",
        ProcessType = AdoProcessType.Agile,
        FieldsCreated = new[] { "Custom.AISessionID" },
        FieldsExisting = Array.Empty<string>(),
        FieldsFailed = Array.Empty<FieldBootstrapFailure>(),
    };
}

// ── Fake services ─────────────────────────────────────────────────────────────

internal sealed class AlwaysSucceedsPreflightService : IAdoTelemetryPreflightService
{
    private readonly PreflightManifestBase _manifest;
    public AlwaysSucceedsPreflightService(PreflightManifestBase manifest) => _manifest = manifest;
    public Task<PreflightResult> RunPreflightAsync(AdoTelemetryFieldConfig? _, CancellationToken __)
        => Task.FromResult(PreflightResult.Succeed(_manifest));
}

internal sealed class AlwaysFailsPreflightService : IAdoTelemetryPreflightService
{
    private readonly string _error;
    public AlwaysFailsPreflightService(string error) => _error = error;
    public Task<PreflightResult> RunPreflightAsync(AdoTelemetryFieldConfig? _, CancellationToken __)
        => Task.FromResult(PreflightResult.Fail(_error));
}

internal sealed class TrackingPreflightService : IAdoTelemetryPreflightService
{
    public int CallCount { get; private set; }
    public Task<PreflightResult> RunPreflightAsync(AdoTelemetryFieldConfig? _, CancellationToken __)
    {
        CallCount++;
        return Task.FromResult(PreflightResult.Succeed(new BootstrapManifest
        {
            Timestamp = DateTimeOffset.UtcNow,
            OrgUrl = "https://dev.azure.com/test",
            Project = "Test",
            ProcessType = AdoProcessType.Agile,
            FieldsCreated = Array.Empty<string>(),
            FieldsExisting = Array.Empty<string>(),
            FieldsFailed = Array.Empty<FieldBootstrapFailure>(),
        }));
    }
}
