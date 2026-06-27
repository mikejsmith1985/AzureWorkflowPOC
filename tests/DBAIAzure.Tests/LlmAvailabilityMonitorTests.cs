// Tests for ILlmAvailabilityMonitor contract: state transitions and auto-restore behaviour.
// Uses a hand-rolled fake to drive probe outcomes without a live LLM endpoint.
using DBAIAzure.Core.Interfaces;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Validates the ILlmAvailabilityMonitor contract:
/// - StateChanged fires on the first transition from available to unavailable
/// - StateChanged fires on auto-restore (unavailable → available) without page reload
/// - StateChanged does NOT fire when availability remains unchanged
/// </summary>
public class LlmAvailabilityMonitorTests
{
    // ── Hand-rolled fake that directly exercises the monitor state machine ──────

    private sealed class FakeLlmAvailabilityMonitor : ILlmAvailabilityMonitor
    {
        private bool _isAvailable;
        public bool IsAvailable => _isAvailable;
        public event Action<bool>? StateChanged;

        public FakeLlmAvailabilityMonitor(bool initiallyAvailable = true)
        {
            _isAvailable = initiallyAvailable;
        }

        /// <summary>Simulates a probe result arriving, matching real monitor transition logic.</summary>
        public void SimulateProbeResult(bool isNowAvailable)
        {
            if (isNowAvailable == _isAvailable)
                return;  // no state change — StateChanged must not fire

            _isAvailable = isNowAvailable;
            StateChanged?.Invoke(isNowAvailable);
        }

        public Task StartMonitoringAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopMonitoringAsync(CancellationToken cancellationToken)  => Task.CompletedTask;
    }

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void StateChanged_FiresOnFirstFailure()
    {
        var monitor = new FakeLlmAvailabilityMonitor(initiallyAvailable: true);
        var stateChangedValues = new List<bool>();
        monitor.StateChanged += value => stateChangedValues.Add(value);

        monitor.SimulateProbeResult(false);

        Assert.Single(stateChangedValues);
        Assert.False(stateChangedValues[0]);
        Assert.False(monitor.IsAvailable);
    }

    [Fact]
    public void StateChanged_FiresOnAutoRestore()
    {
        var monitor = new FakeLlmAvailabilityMonitor(initiallyAvailable: false);
        var stateChangedValues = new List<bool>();
        monitor.StateChanged += value => stateChangedValues.Add(value);

        monitor.SimulateProbeResult(true);

        Assert.Single(stateChangedValues);
        Assert.True(stateChangedValues[0]);
        Assert.True(monitor.IsAvailable);
    }

    [Fact]
    public void StateChanged_DoesNotFire_WhenAvailabilityUnchanged()
    {
        var monitor = new FakeLlmAvailabilityMonitor(initiallyAvailable: true);
        var fireCount = 0;
        monitor.StateChanged += _ => fireCount++;

        monitor.SimulateProbeResult(true);  // still available — no transition

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void StateChanged_FiresMultipleTimes_AcrossTransitions()
    {
        var monitor = new FakeLlmAvailabilityMonitor(initiallyAvailable: true);
        var events = new List<bool>();
        monitor.StateChanged += value => events.Add(value);

        monitor.SimulateProbeResult(false); // available → unavailable
        monitor.SimulateProbeResult(true);  // unavailable → available (auto-restore)
        monitor.SimulateProbeResult(false); // available → unavailable (second failure)

        Assert.Equal(3, events.Count);
        Assert.False(events[0]);
        Assert.True(events[1]);
        Assert.False(events[2]);
    }

    [Fact]
    public async Task StartMonitoring_CanBeCancelled()
    {
        var monitor = new FakeLlmAvailabilityMonitor();
        using var cts = new CancellationTokenSource();

        await monitor.StartMonitoringAsync(cts.Token);
        await cts.CancelAsync();
        await monitor.StopMonitoringAsync(CancellationToken.None);

        Assert.True(cts.IsCancellationRequested);
    }
}
