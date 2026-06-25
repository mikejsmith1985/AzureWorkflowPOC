// Shared in-memory SQLite test harness for the repo-app feature (feature 013).
using DBAIAzure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Tests.Apps;

/// <summary>
/// Builds an isolated in-memory SQLite database (real SQLite, not the EF InMemory provider, so unique
/// indexes are enforced) and an <see cref="IDbContextFactory{PipelineDbContext}"/> over it. The caller
/// owns the returned connection and must dispose it to release the database.
/// </summary>
internal static class AppTestDb
{
    /// <summary>Creates a schema-applied in-memory database and a factory bound to it.</summary>
    internal static (IDbContextFactory<PipelineDbContext> factory, SqliteConnection keepAlive) Create()
    {
        var keepAlive = new SqliteConnection("Data Source=:memory:");
        keepAlive.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(keepAlive)
            .Options;

        using (var seed = new PipelineDbContext(options))
            seed.Database.EnsureCreated();

        return (new Factory(options), keepAlive);
    }

    /// <summary>Creates a temp directory that exists on disk (a valid repo path for registration tests).</summary>
    internal static DirectoryInfo CreateTempRepo() => Directory.CreateTempSubdirectory("app013-");

    private sealed class Factory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);

        public ValueTask<PipelineDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new PipelineDbContext(options));
    }
}
