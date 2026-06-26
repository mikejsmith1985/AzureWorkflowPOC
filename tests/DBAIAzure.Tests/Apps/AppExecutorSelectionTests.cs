// Unit tests for executor selection (Docker when available, else Sim) (feature 013, US4).
using DBAIAzure.Connectors.Apps;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>Verifies the executor-selection rule prefers Docker only when available and not in demo mode.</summary>
public sealed class AppExecutorSelectionTests
{
    private sealed class StubExecutor(string kind) : IAppExecutor
    {
        public string ExecutorKind => kind;
        public Task BuildAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RunAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly IAppExecutor Docker = new StubExecutor("Docker");
    private static readonly IAppExecutor Sim = new StubExecutor("Simulated");

    [Fact]
    public void DockerAvailable_NotDemo_SelectsDocker()
        => Assert.Equal("Docker", AppExecutorSelector.Select(dockerAvailable: true, demoMode: false, Docker, Sim).ExecutorKind);

    [Fact]
    public void DockerUnavailable_SelectsSim()
        => Assert.Equal("Simulated", AppExecutorSelector.Select(dockerAvailable: false, demoMode: false, Docker, Sim).ExecutorKind);

    [Fact]
    public void DemoMode_SelectsSim_EvenWhenDockerAvailable()
        => Assert.Equal("Simulated", AppExecutorSelector.Select(dockerAvailable: true, demoMode: true, Docker, Sim).ExecutorKind);
}
