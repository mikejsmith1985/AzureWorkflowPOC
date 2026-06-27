// Env-gated integration test: real Docker build/run in throwaway containers (feature 013, US2).
// Runs only when DBAI_DOCKER_TESTS=1 and a Docker engine is reachable; otherwise it is skipped so the
// unit suite stays hermetic (Article V: integration tests use real infrastructure, not mocks).
using DBAIAzure.Connectors.Apps;
using DBAIAzure.Core.Models;
using DBAIAzure.Tests.Apps;
using Xunit;

namespace DBAIAzure.Tests.Integration;

/// <summary>
/// Exercises <see cref="DockerAppExecutor"/> against a real Docker engine: a build and a run of a tiny
/// repo in disposable containers, asserting captured logs, recorded outcomes, and that no container is
/// left behind. Gated behind <c>DBAI_DOCKER_TESTS=1</c> + engine reachability.
/// </summary>
public sealed class DockerAppExecutorTests
{
    private static bool Gated => Environment.GetEnvironmentVariable("DBAI_DOCKER_TESTS") == "1";

    [Fact]
    public async Task Build_ThenRun_SucceedsAndCleansUp()
    {
        if (!Gated || !AppExecutorSelector.TryConnectDocker(out var client) || client is null)
            return; // skipped: no opt-in or no engine

        var repo = Directory.CreateTempSubdirectory("docker-fixture-");
        await File.WriteAllTextAsync(Path.Combine(repo.FullName, "marker.txt"), "hello");

        var registry = new FakeAppRegistry();
        var notifier = new FakeAppStatusNotifier();
        using (client)
        {
            var sut = new DockerAppExecutor(registry, notifier, client);
            var app = registry.Add(new MonitoredApp
            {
                Name = "intg", OwnerId = "demo", RepoLocalPath = repo.FullName,
                BuildCommand = "echo built > artifact.txt && cat marker.txt",
                RunCommand = "cat artifact.txt"
            });

            await sut.BuildAsync(app, new AppExecutionRequest(app.AppId, app.Name, app.RepoLocalPath, null, app.BuildCommand!, ExecutionMode.Build, 120));
            var afterBuild = await registry.GetAsync(app.AppId);
            Assert.Equal(AppStatus.Ready, afterBuild!.Status);
            Assert.True(afterBuild.LastBuildResult!.Succeeded, afterBuild.LastBuildResult.Logs);
            Assert.Contains("hello", afterBuild.LastBuildResult.Logs);

            await sut.RunAsync(afterBuild, new AppExecutionRequest(app.AppId, app.Name, app.RepoLocalPath, null, app.RunCommand, ExecutionMode.Run, 120));
            var afterRun = await registry.GetAsync(app.AppId);
            Assert.Equal(AppStatus.Ready, afterRun!.Status);
            Assert.Equal(RunOutcome.Succeeded, afterRun.LastRunResult!.Outcome);
            Assert.Contains("built", afterRun.LastRunResult.Logs);

            // Cleanup: no container should remain labelled for this app (throwaway, FR-007).
            var remaining = await client.Containers.ListContainersAsync(new Docker.DotNet.Models.ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [$"dbai.app013={app.AppId}"] = true }
                }
            });
            Assert.Empty(remaining);
        }

        repo.Delete(recursive: true);
    }

    [Fact]
    public async Task Run_ExceedingTimeout_RecordedAsTimedOut()
    {
        if (!Gated || !AppExecutorSelector.TryConnectDocker(out var client) || client is null)
            return;

        var repo = Directory.CreateTempSubdirectory("docker-timeout-");
        var registry = new FakeAppRegistry();
        using (client)
        {
            var sut = new DockerAppExecutor(registry, new FakeAppStatusNotifier(), client);
            var app = registry.Add(new MonitoredApp
            {
                Name = "slow", OwnerId = "demo", RepoLocalPath = repo.FullName, RunCommand = "sleep 30", Status = AppStatus.Ready
            });

            await sut.RunAsync(app, new AppExecutionRequest(app.AppId, app.Name, app.RepoLocalPath, null, "sleep 30", ExecutionMode.Run, TimeoutSeconds: 2));

            var after = await registry.GetAsync(app.AppId);
            Assert.Equal(AppStatus.Ready, after!.Status); // never stuck in Running
            Assert.Equal(RunOutcome.TimedOut, after.LastRunResult!.Outcome);
        }
        repo.Delete(recursive: true);
    }
}
