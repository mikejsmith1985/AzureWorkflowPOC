// Integration tests for DemoConnectorSeeder against the real SqliteConnectorConfigRepository, a real
// in-memory SQLite database, and real ASP.NET Data Protection. Proves the seeded rows persist with the
// UI's JSON shapes, their secrets round-trip through encrypt/decrypt, and the LLM connector is absent.
using DBAIAzure.Core.Configuration;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class DemoConnectorSeederIntegrationTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly SqliteConnectorConfigRepository _repository;

    public DemoConnectorSeederIntegrationTests()
    {
        _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
        _keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_keepAliveConnection)
            .Options;

        using (var seedContext = new PipelineDbContext(options))
        {
            seedContext.Database.EnsureCreated();
            seedContext.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS ConnectorConfigs (
                    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConnectorType        TEXT    NOT NULL,
                    ConfigJson           TEXT,
                    EncryptedSecretsJson TEXT,
                    IsConfigured         INTEGER NOT NULL DEFAULT 0,
                    LastUpdatedAt        TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00',
                    LastTestResult       TEXT,
                    LastTestMessage      TEXT,
                    LastTestedAt         TEXT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ConnectorConfigs_ConnectorType
                    ON ConnectorConfigs (ConnectorType);
                """);
        }

        var protector = new DataProtectionSecretProtector(DataProtectionProvider.Create("DBAIAzure.Tests"));
        _repository = new SqliteConnectorConfigRepository(new SharedConnectionFactory(options), protector);
    }

    [Fact]
    public async Task SeedAsync_PersistsBackOfficeConnectors_WithSecretsRoundTrippingAndNoLlm()
    {
        var seeder = new DemoConnectorSeeder(_repository, Options.Create(FullOptions()), NullLogger<DemoConnectorSeeder>.Instance);

        await seeder.SeedAsync();

        var serviceNow = await _repository.GetAsync(ConnectorType.ServiceNow);
        Assert.NotNull(serviceNow);
        Assert.True(serviceNow!.IsConfigured);
        Assert.Equal("""{"instanceUrl":"https://dev999.service-now.com","username":"svc"}""", serviceNow.NonSecretConfig);
        Assert.Equal("""{"password":"pw-secret"}""", await _repository.GetDecryptedSecretsAsync(ConnectorType.ServiceNow));

        var ado = await _repository.GetAsync(ConnectorType.AzureDevOps);
        Assert.True(ado!.IsConfigured);
        Assert.Equal("""{"personalAccessToken":"pat-secret"}""", await _repository.GetDecryptedSecretsAsync(ConnectorType.AzureDevOps));

        var messaging = await _repository.GetAsync(ConnectorType.Messaging);
        Assert.True(messaging!.IsConfigured);

        // The LLM connector must never be seeded.
        Assert.Null(await _repository.GetAsync(ConnectorType.LLM));
    }

    private static ConnectorSeedOptions FullOptions() => new()
    {
        ServiceNow = new ServiceNowSeed { InstanceUrl = "https://dev999.service-now.com", Username = "svc", Password = "pw-secret" },
        AzureDevOps = new AzureDevOpsSeed { OrganizationUrl = "https://dev.azure.com/contoso", ProjectName = "Platform", PersonalAccessToken = "pat-secret" },
        Messaging = new MessagingSeed { Platform = "Teams", McpServerUrl = "https://mcp.example.com/sse", McpToolName = "send", McpAuthToken = "mcp-secret" },
    };

    public void Dispose() => _keepAliveConnection.Dispose();

    /// <summary>Hands out contexts over the single keep-alive in-memory connection (mirrors the repository tests).</summary>
    private sealed class SharedConnectionFactory : IDbContextFactory<PipelineDbContext>
    {
        private readonly DbContextOptions<PipelineDbContext> _options;
        public SharedConnectionFactory(DbContextOptions<PipelineDbContext> options) => _options = options;
        public PipelineDbContext CreateDbContext() => new(_options);
    }

    /// <summary>Adapts a real <see cref="IDataProtectionProvider"/> to the app's <see cref="ISecretProtector"/> seam.</summary>
    private sealed class DataProtectionSecretProtector : ISecretProtector
    {
        private readonly IDataProtector _protector;
        public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
            _protector = provider.CreateProtector("connector-secrets");
        public string Protect(string plaintext) => _protector.Protect(plaintext);
        public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
    }
}
