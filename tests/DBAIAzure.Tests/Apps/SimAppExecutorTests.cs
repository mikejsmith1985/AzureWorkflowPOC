// Unit tests for SimAppExecutor — synthesized outcomes, never hangs (feature 013, US2).
using DBAIAzure.Connectors.Apps;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>
/// Verifies the simulated executor drives the full lifecycle (Building → Ready, Running → Ready) with
/// synthesized summaries/logs, notifies the UI, and never hangs — fully mocked, no I/O.
/// </summary>
public sealed class SimAppExecutorTests
{
    private static MonitoredApp NewReadyApp() => new()
    {
        Name = "sim", OwnerId = "demo", RepoLocalPath = "/x", RunCommand = "run", Status = AppStatus.Registered
    };

    private static AppExecutionRequest Req(MonitoredApp app, ExecutionMode mode) =>
        new(app.AppId, app.Name, app.RepoLocalPath, app.Branch, "cmd", mode, TimeoutSeconds: 5);

    [Fact]
    public async Task BuildAsync_MovesBuildingThenReady_AndRecordsLogs()
    {
        var registry = new FakeAppRegistry();
        var notifier = new FakeAppStatusNotifier();
        var app = registry.Add(NewReadyApp());
        var sut = new SimAppExecutor(registry, notifier, TimeSpan.Zero);

        await sut.BuildAsync(app, Req(app, ExecutionMode.Build));

        var reloaded = await registry.GetAsync(app.AppId);
        Assert.Equal(AppStatus.Ready, reloaded!.Status);
        Assert.True(reloaded.LastBuildResult!.Succeeded);
        Assert.Contains("simulated", reloaded.LastBuildResult.Logs);
        Assert.Contains(AppStatus.Building, registry.StatusHistory);
        Assert.NotEmpty(notifier.Notifications);
    }

    [Fact]
    public async Task RunAsync_MovesRunningThenReady_WithSuccessOutcome()
    {
        var registry = new FakeAppRegistry();
        var notifier = new FakeAppStatusNotifier();
        var app = registry.Add(NewReadyApp() with { Status = AppStatus.Ready });
        var sut = new SimAppExecutor(registry, notifier, TimeSpan.Zero);

        await sut.RunAsync(app, Req(app, ExecutionMode.Run));

        var reloaded = await registry.GetAsync(app.AppId);
        Assert.Equal(AppStatus.Ready, reloaded!.Status);
        Assert.Equal(RunOutcome.Succeeded, reloaded.LastRunResult!.Outcome);
        Assert.Contains(AppStatus.Running, registry.StatusHistory);
    }

    [Fact]
    public void ExecutorKind_IsSimulated()
    {
        var sut = new SimAppExecutor(new FakeAppRegistry(), new FakeAppStatusNotifier(), TimeSpan.Zero);
        Assert.Equal("Simulated", sut.ExecutorKind);
    }

    [Fact]
    public async Task BuildAsync_CompletesQuickly_NeverHangs()
    {
        var sut = new SimAppExecutor(new FakeAppRegistry(), new FakeAppStatusNotifier(), TimeSpan.Zero);
        var app = NewReadyApp();
        var build = sut.BuildAsync(app, Req(app, ExecutionMode.Build));

        var finished = await Task.WhenAny(build, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(build, finished);
    }
}
