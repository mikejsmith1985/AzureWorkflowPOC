// Tests for the one-time ADO->WorkTracker connector migration (spec-020, T004/T035).
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Entities;
using DBAIAzure.Storage.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

/// <summary>
/// Verifies the migration converts an existing Azure DevOps connector into the generic WorkTracker connector
/// (provider injected, secret preserved), is idempotent on replay, and is a no-op on a fresh install.
/// </summary>
public sealed class WorkTrackerMigrationTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;

    public WorkTrackerMigrationTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using var seed = new PipelineDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task Migrates_LegacyAdoRow_InjectsProvider_AndPreservesSecret()
    {
        await using (var db = new PipelineDbContext(_options))
        {
            db.ConnectorConfigs.Add(new ConnectorConfigRecord
            {
                ConnectorType = nameof(ConnectorType.AzureDevOps),
                ConfigJson = """{"organizationUrl":"https://dev.azure.com/o","projectName":"P"}""",
                EncryptedSecretsJson = "CIPHERTEXT",
                IsConfigured = true,
                LastUpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new PipelineDbContext(_options))
            Assert.True(await WorkTrackerConnectorMigration.MigrateAsync(db));

        await using (var db = new PipelineDbContext(_options))
        {
            var wt = await db.ConnectorConfigs.SingleAsync(r => r.ConnectorType == nameof(ConnectorType.WorkTracker));
            Assert.Equal("CIPHERTEXT", wt.EncryptedSecretsJson);   // ciphertext copied verbatim
            Assert.True(wt.IsConfigured);
            using var doc = JsonDocument.Parse(wt.ConfigJson!);
            Assert.Equal("AzureDevOps", doc.RootElement.GetProperty("provider").GetString());
            Assert.Equal("https://dev.azure.com/o", doc.RootElement.GetProperty("organizationUrl").GetString());
        }
    }

    [Fact]
    public async Task IsIdempotent_SecondRunIsNoOp()
    {
        await using (var db = new PipelineDbContext(_options))
        {
            db.ConnectorConfigs.Add(new ConnectorConfigRecord
            {
                ConnectorType = nameof(ConnectorType.AzureDevOps),
                ConfigJson = """{"organizationUrl":"u","projectName":"P"}""",
                EncryptedSecretsJson = "C", IsConfigured = true, LastUpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new PipelineDbContext(_options))
            Assert.True(await WorkTrackerConnectorMigration.MigrateAsync(db));
        await using (var db = new PipelineDbContext(_options))
            Assert.False(await WorkTrackerConnectorMigration.MigrateAsync(db));   // replay = no-op

        await using (var verify = new PipelineDbContext(_options))
            Assert.Equal(1, await verify.ConnectorConfigs.CountAsync(r => r.ConnectorType == nameof(ConnectorType.WorkTracker)));
    }

    [Fact]
    public async Task FreshInstall_NoAdoRow_DoesNothing()
    {
        await using var db = new PipelineDbContext(_options);
        Assert.False(await WorkTrackerConnectorMigration.MigrateAsync(db));
        Assert.Equal(0, await db.ConnectorConfigs.CountAsync());
    }
}
