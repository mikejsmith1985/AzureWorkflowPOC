// SK Process step wrapping the ADO telemetry preflight service (spec-009, T032).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0080

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Semantic Kernel process step that runs the ADO telemetry field preflight.
/// On success, emits <c>AdoPreflightSucceeded</c> with the manifest path.
/// On failure, emits <c>AdoPreflightFailed</c> with the error message.
/// Callers wire these events to subsequent pipeline steps.
/// </summary>
public sealed class AdoTelemetryPreflightStep : KernelProcessStep<AdoPreflightStepState>
{
    /// <summary>
    /// Input event that triggers the preflight.
    /// Payload is an optional <see cref="AdoTelemetryFieldConfig"/> override; null uses the embedded default.
    /// </summary>
    public const string AdoPreflightRequested = nameof(AdoPreflightRequested);

    /// <summary>Output event emitted when the preflight completes successfully. Payload is the manifest file path.</summary>
    public const string AdoPreflightSucceeded = nameof(AdoPreflightSucceeded);

    /// <summary>Output event emitted when the preflight fails. Payload is the error message string.</summary>
    public const string AdoPreflightFailed = nameof(AdoPreflightFailed);

    private AdoPreflightStepState _state = new();

    /// <inheritdoc/>
    public override ValueTask ActivateAsync(KernelProcessStepState<AdoPreflightStepState> state)
    {
        _state = state.State ?? new AdoPreflightStepState();
        return ValueTask.CompletedTask;
    }

    /// <summary>Returns the current step state. Exposed for unit tests (KernelProcessStepContext is sealed).</summary>
    public AdoPreflightStepState GetCurrentState() => _state;

    /// <summary>
    /// Runs the preflight service and emits the appropriate output event.
    /// The step is idempotent — if already complete, it re-emits <c>AdoPreflightSucceeded</c>
    /// without calling the service again.
    /// </summary>

    [KernelFunction(AdoPreflightRequested)]
    public async Task RunPreflightAsync(
        KernelProcessStepContext? context,
        IAdoTelemetryPreflightService preflightService,
        AdoTelemetryFieldConfig? overrideConfig,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        if (_state.IsComplete)
        {
            if (context is not null)
                await context.EmitEventAsync(AdoPreflightSucceeded, _state.ManifestPath);
            return;
        }

        var result = await preflightService.RunPreflightAsync(overrideConfig, cancellationToken);

        if (result.IsSuccess)
        {
            _state = _state with
            {
                Mode = result.Manifest?.Mode,
                IsComplete = true,
            };
            // context is null only in unit tests (KernelProcessStepContext is sealed — cannot be mocked).
            if (context is not null)
                await context.EmitEventAsync(AdoPreflightSucceeded, data: result);
        }
        else
        {
            if (context is not null)
                await context.EmitEventAsync(AdoPreflightFailed, data: result.ErrorMessage);
        }
    }
}

/// <summary>
/// Persisted state for <see cref="AdoTelemetryPreflightStep"/>.
/// Survives process suspension and restart.
/// </summary>
public sealed record AdoPreflightStepState
{
    /// <summary>Absolute path to the manifest file written by the preflight. Set after a successful run.</summary>
    public string? ManifestPath { get; init; }

    /// <summary>The preflight mode that was selected (Bootstrap or Adaptive). Set after success.</summary>
    public PreflightMode? Mode { get; init; }

    /// <summary>True once the preflight has completed (either Bootstrap or Adaptive). Prevents re-runs.</summary>
    public bool IsComplete { get; init; }
}
