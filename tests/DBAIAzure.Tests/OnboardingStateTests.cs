// Unit tests for the first-run onboarding state: the banner shows only when the LLM connector is not
// yet healthy and the user hasn't dismissed it; a health-check exception is treated as "not healthy".
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class OnboardingStateTests
{
    [Theory]
    [InlineData(false, false, true)]  // unhealthy + not dismissed → show
    [InlineData(true, false, false)]  // healthy → hide
    [InlineData(false, true, false)]  // dismissed → hide
    [InlineData(true, true, false)]   // healthy + dismissed → hide
    public void ShouldShow_ReflectsHealthAndDismissal(bool isLlmHealthy, bool isDismissed, bool expected)
    {
        var state = new OnboardingState(isLlmHealthy, isDismissed);
        Assert.Equal(expected, state.ShouldShow);
    }

    [Fact]
    public async Task InitialiseAsync_HealthyLlm_HidesBanner()
    {
        var service = Build(llmHealthy: true);
        await service.InitialiseAsync();

        Assert.True(service.State.IsLlmHealthy);
        Assert.False(service.State.ShouldShow);
    }

    [Fact]
    public async Task InitialiseAsync_UnhealthyLlm_ShowsBanner()
    {
        var service = Build(llmHealthy: false);
        await service.InitialiseAsync();

        Assert.False(service.State.IsLlmHealthy);
        Assert.True(service.State.ShouldShow);
    }

    [Fact]
    public async Task InitialiseAsync_HealthCheckThrows_TreatedAsUnhealthy()
    {
        var service = Build(healthChecker: new ThrowingHealthChecker());
        await service.InitialiseAsync();

        Assert.False(service.State.IsLlmHealthy);
        Assert.True(service.State.ShouldShow);
    }

    [Fact]
    public async Task InitialiseAsync_PreviouslyDismissed_HidesBannerEvenWhenUnhealthy()
    {
        var service = Build(llmHealthy: false, storedDismissal: "true");
        await service.InitialiseAsync();

        Assert.True(service.State.IsDismissed);
        Assert.False(service.State.ShouldShow);
    }

    [Fact]
    public async Task DismissAsync_HidesBanner()
    {
        var service = Build(llmHealthy: false);
        await service.InitialiseAsync();
        Assert.True(service.State.ShouldShow);

        await service.DismissAsync();

        Assert.True(service.State.IsDismissed);
        Assert.False(service.State.ShouldShow);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static OnboardingStateService Build(
        bool llmHealthy = false,
        string? storedDismissal = null,
        IConnectorHealthChecker? healthChecker = null) =>
        new(
            healthChecker ?? new StubHealthChecker(llmHealthy),
            new FakeJsRuntime { StoredValue = storedDismissal },
            NullLogger<OnboardingStateService>.Instance);

    /// <summary>Returns a fixed success/failure result for any connector.</summary>
    private sealed class StubHealthChecker : IConnectorHealthChecker
    {
        private readonly bool _isSuccess;
        public StubHealthChecker(bool isSuccess) => _isSuccess = isSuccess;

        public Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(new ConnectorTestResult(type, _isSuccess, "stub", DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Throws on every test — exercises the exception-safe "treat as unhealthy" path.</summary>
    private sealed class ThrowingHealthChecker : IConnectorHealthChecker
    {
        public Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Minimal IJSRuntime: returns a configured value for localStorageGet, no-ops everything else.</summary>
    private sealed class FakeJsRuntime : IJSRuntime
    {
        public string? StoredValue { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            object? result = identifier == "localStorageGet" ? StoredValue : default(TValue);
            return new ValueTask<TValue>((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
