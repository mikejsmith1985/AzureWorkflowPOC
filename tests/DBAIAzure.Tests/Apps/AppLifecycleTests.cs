// Unit tests for the repo-app status lifecycle (feature 013, US1 Registered-state + US2 transitions).
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Repositories;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>
/// Exercises the <see cref="AppStatus"/> lifecycle on <see cref="SqliteAppRegistryRepository"/>:
/// the Registered initial state and reload (US1), legal transitions, the never-stuck guarantee
/// after a build/run result (FR-008), and the single-in-flight concurrency guard (FR-016).
/// </summary>
public sealed class AppLifecycleTests
{
    private const string Owner = "demo";

    private static async Task<(SqliteAppRegistryRepository sut, string appId, DirectoryInfo repo)> SeedAsync(
        Microsoft.EntityFrameworkCore.IDbContextFactory<DBAIAzure.Storage.PipelineDbContext> factory)
    {
        var repo = AppTestDb.CreateTempRepo();
        var sut = new SqliteAppRegistryRepository(factory);
        var saved = await sut.RegisterAsync(new MonitoredApp
        {
            Name = "app", OwnerId = Owner, RepoLocalPath = repo.FullName, RunCommand = "run"
        });
        return (sut, saved.AppId, repo);
    }

    [Fact]
    public async Task NewlyRegistered_IsRegistered_AndReloadsIntact()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var (sut, appId, repo) = await SeedAsync(factory);

            var reloaded = await sut.GetAsync(appId);

            Assert.NotNull(reloaded);
            Assert.Equal(AppStatus.Registered, reloaded!.Status);
            Assert.Null(reloaded.LastBuildResult);
            Assert.Null(reloaded.LastRunResult);
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BuildResult_Success_MovesToReady_AndPersistsLogs()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var (sut, appId, repo) = await SeedAsync(factory);
            await sut.SetStatusAsync(appId, AppStatus.Building);

            await sut.SetBuildResultAsync(appId, new AppBuildResult(true, "ok", "build log", DateTimeOffset.UtcNow));

            var reloaded = await sut.GetAsync(appId);
            Assert.Equal(AppStatus.Ready, reloaded!.Status);
            Assert.Equal("build log", reloaded.LastBuildResult!.Logs);
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BuildResult_Failure_MovesToBuildFailed()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var (sut, appId, repo) = await SeedAsync(factory);
            await sut.SetStatusAsync(appId, AppStatus.Building);

            await sut.SetBuildResultAsync(appId, new AppBuildResult(false, "boom", "err", DateTimeOffset.UtcNow));

            Assert.Equal(AppStatus.BuildFailed, (await sut.GetAsync(appId))!.Status);
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunResult_AnyOutcome_ReturnsToReady_NeverStuck()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var (sut, appId, repo) = await SeedAsync(factory);
            await sut.SetBuildResultAsync(appId, new AppBuildResult(true, "ok", "", DateTimeOffset.UtcNow));
            await sut.SetStatusAsync(appId, AppStatus.Running);

            // Even a timeout must return the app to Ready (FR-008) — never left stuck in Running.
            await sut.SetRunResultAsync(appId, new AppRunResult(RunOutcome.TimedOut, "timed out", "", DateTimeOffset.UtcNow));

            var reloaded = await sut.GetAsync(appId);
            Assert.Equal(AppStatus.Ready, reloaded!.Status);
            Assert.Equal(RunOutcome.TimedOut, reloaded.LastRunResult!.Outcome);
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SetStatus_BuildWhileBuilding_IsRejected_SingleInFlight()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var (sut, appId, repo) = await SeedAsync(factory);
            await sut.SetStatusAsync(appId, AppStatus.Building);

            // A second concurrent build trigger must be rejected (FR-016).
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.SetStatusAsync(appId, AppStatus.Building));
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SetStatus_IllegalTransition_Throws()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var (sut, appId, repo) = await SeedAsync(factory);

            // Registered → Running is not allowed (must build first).
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.SetStatusAsync(appId, AppStatus.Running));
            repo.Delete(recursive: true);
        }
    }
}
