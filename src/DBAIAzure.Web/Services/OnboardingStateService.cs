// First-run onboarding state: decides whether the "configure your LLM" banner should appear.
// The banner guides a first-time visitor to set up the one required connector (the LLM key) and
// disappears once the LLM is healthy or the user dismisses it (persisted in browser localStorage).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.JSInterop;

namespace DBAIAzure.Web.Services;

/// <summary>Immutable snapshot of onboarding banner state for one visitor session.</summary>
public sealed record OnboardingState(bool IsLlmHealthy, bool IsDismissed)
{
    /// <summary>The banner shows only while the LLM is not yet healthy and the user has not dismissed it.</summary>
    public bool ShouldShow => !IsLlmHealthy && !IsDismissed;
}

/// <summary>
/// Computes and holds the first-run onboarding state for the current session. The LLM connector is
/// the single required step; the banner appears until that connector is healthy or the user dismisses it.
/// </summary>
public interface IOnboardingStateService
{
    /// <summary>The current onboarding snapshot. Defaults to "show" until <see cref="InitialiseAsync"/> runs.</summary>
    OnboardingState State { get; }

    /// <summary>Reads the persisted dismissal flag and runs a live LLM health check to compute the state.</summary>
    Task InitialiseAsync(CancellationToken cancellationToken = default);

    /// <summary>Permanently dismisses the banner for this browser (persisted to localStorage).</summary>
    Task DismissAsync();
}

/// <inheritdoc />
public sealed class OnboardingStateService : IOnboardingStateService
{
    // localStorage key holding "true" once the visitor has dismissed the onboarding banner.
    private const string DismissedStorageKey = "onboarding_dismissed";
    private const string DismissedValue = "true";

    private readonly IConnectorHealthChecker _healthChecker;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<OnboardingStateService> _logger;

    /// <inheritdoc />
    public OnboardingState State { get; private set; } = new(IsLlmHealthy: false, IsDismissed: false);

    /// <summary>Creates the service from the connector health checker and the JS interop runtime.</summary>
    public OnboardingStateService(
        IConnectorHealthChecker healthChecker,
        IJSRuntime jsRuntime,
        ILogger<OnboardingStateService> logger)
    {
        _healthChecker = healthChecker;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var isDismissed = await ReadDismissedAsync();
        var isLlmHealthy = await CheckLlmHealthyAsync(cancellationToken);
        State = new OnboardingState(isLlmHealthy, isDismissed);
    }

    /// <inheritdoc />
    public async Task DismissAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorageSet", DismissedStorageKey, DismissedValue);
        }
        catch (Exception ex)
        {
            // A failed persist must not break the page — the banner simply reappears next load.
            _logger.LogWarning(ex, "Could not persist onboarding dismissal to localStorage.");
        }
        State = State with { IsDismissed = true };
    }

    /// <summary>
    /// Runs a live LLM health check. Any failure or exception is treated as "not healthy" so the
    /// banner stays visible — a first-time user with no key must always be guided, never silently skipped.
    /// </summary>
    private async Task<bool> CheckLlmHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _healthChecker.TestAsync(ConnectorType.LLM, cancellationToken);
            return result.IsSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM health check threw during onboarding initialisation — treating as unhealthy.");
            return false;
        }
    }

    /// <summary>Reads the persisted dismissal flag; a missing or unreadable value means "not dismissed".</summary>
    private async Task<bool> ReadDismissedAsync()
    {
        try
        {
            var stored = await _jsRuntime.InvokeAsync<string?>("localStorageGet", DismissedStorageKey);
            return string.Equals(stored, DismissedValue, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Pre-render (no JS yet) or storage unavailable — default to showing the banner.
            return false;
        }
    }
}
