// Unit tests for repo-app registration validation and persistence (feature 013, US1).
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Repositories;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>
/// Validates <see cref="SqliteAppRegistryRepository"/> registration rules (FR-002): a duplicate name
/// per owner, a non-existent path, and a missing run command are each rejected with a clear message;
/// a valid registration persists and is owner-scoped.
/// </summary>
public sealed class AppRegistryValidationTests
{
    private const string Owner = "demo";

    private static MonitoredApp NewApp(string name, string repoPath, string runCommand = "npm start") => new()
    {
        Name = name,
        OwnerId = Owner,
        RepoLocalPath = repoPath,
        RunCommand = runCommand
    };

    [Fact]
    public async Task RegisterAsync_ValidApp_PersistsAsRegistered()
    {
        var (factory, connection) = AppTestDb.Create();
        var repo = new DirectoryInfo(AppTestDb.CreateTempRepo().FullName);
        await using (connection)
        {
            var sut = new SqliteAppRegistryRepository(factory);

            var saved = await sut.RegisterAsync(NewApp("alpha", repo.FullName));

            var reloaded = await sut.GetAsync(saved.AppId);
            Assert.NotNull(reloaded);
            Assert.Equal("alpha", reloaded!.Name);
            Assert.Equal(AppStatus.Registered, reloaded.Status);
            Assert.Equal(repo.FullName, reloaded.RepoLocalPath);
        }
        repo.Delete(recursive: true);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateNameForOwner_Throws()
    {
        var (factory, connection) = AppTestDb.Create();
        var repo = AppTestDb.CreateTempRepo();
        await using (connection)
        {
            var sut = new SqliteAppRegistryRepository(factory);
            await sut.RegisterAsync(NewApp("dupe", repo.FullName));

            await Assert.ThrowsAsync<AppRegistrationException>(
                () => sut.RegisterAsync(NewApp("dupe", repo.FullName)));
        }
        repo.Delete(recursive: true);
    }

    [Fact]
    public async Task RegisterAsync_NonExistentPath_Throws()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var sut = new SqliteAppRegistryRepository(factory);
            var missing = Path.Combine(Path.GetTempPath(), "app013-does-not-exist-" + Guid.NewGuid());

            await Assert.ThrowsAsync<AppRegistrationException>(
                () => sut.RegisterAsync(NewApp("ghost", missing)));
        }
    }

    [Fact]
    public async Task RegisterAsync_MissingRunCommand_Throws()
    {
        var (factory, connection) = AppTestDb.Create();
        var repo = AppTestDb.CreateTempRepo();
        await using (connection)
        {
            var sut = new SqliteAppRegistryRepository(factory);

            await Assert.ThrowsAsync<AppRegistrationException>(
                () => sut.RegisterAsync(NewApp("nocmd", repo.FullName, runCommand: "")));
        }
        repo.Delete(recursive: true);
    }

    [Fact]
    public async Task ListByOwnerAsync_ReturnsOnlyOwnersApps()
    {
        var (factory, connection) = AppTestDb.Create();
        var repo = AppTestDb.CreateTempRepo();
        await using (connection)
        {
            var sut = new SqliteAppRegistryRepository(factory);
            await sut.RegisterAsync(NewApp("mine", repo.FullName));
            await sut.RegisterAsync(new MonitoredApp
            {
                Name = "theirs", OwnerId = "other", RepoLocalPath = repo.FullName, RunCommand = "x"
            });

            var mine = await sut.ListByOwnerAsync(Owner);

            Assert.Single(mine);
            Assert.Equal("mine", mine[0].Name);
        }
        repo.Delete(recursive: true);
    }

    [Fact]
    public async Task RemoveAsync_Unregisters()
    {
        var (factory, connection) = AppTestDb.Create();
        var repo = AppTestDb.CreateTempRepo();
        await using (connection)
        {
            var sut = new SqliteAppRegistryRepository(factory);
            var saved = await sut.RegisterAsync(NewApp("temp", repo.FullName));

            await sut.RemoveAsync(saved.AppId);

            Assert.Null(await sut.GetAsync(saved.AppId));
            Assert.Empty(await sut.ListByOwnerAsync(Owner));
        }
        repo.Delete(recursive: true);
    }

    [Fact]
    public async Task ExistsByNameAsync_TrueAfterRegister()
    {
        var (factory, connection) = AppTestDb.Create();
        var repo = AppTestDb.CreateTempRepo();
        await using (connection)
        {
            var sut = new SqliteAppRegistryRepository(factory);
            await sut.RegisterAsync(NewApp("seen", repo.FullName));

            Assert.True(await sut.ExistsByNameAsync(Owner, "seen"));
            Assert.False(await sut.ExistsByNameAsync(Owner, "unseen"));
        }
        repo.Delete(recursive: true);
    }
}
