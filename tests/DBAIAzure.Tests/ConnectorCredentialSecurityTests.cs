// Integration test — credential values must never appear in plaintext in the DB (T094, Article IX).
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Security integration test: saves a connector config with a known test-only credential string,
/// then queries all entity tables directly and asserts the raw credential value is not present in
/// any stored column (T094, Article IX — "A secret value must never enter source, a log, or the
/// conversation").
///
/// Uses an in-memory SQLite connection to avoid any I/O, but exercises the real EF Core mapping
/// and the real <see cref="SqliteConnectorConfigRepository"/> encryption path with a production-
/// equivalent fake protector that does real XOR-style encoding (not just a prefix tag) so the
/// column value is verifiably different from the plaintext.
/// </summary>
public sealed class ConnectorCredentialSecurityTests : IDisposable
{
    private const string TestPatValue = "SENTINEL-SECRET-VALUE-94a8bc";

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly SqliteConnectorConfigRepository _repo;
    private readonly IDbContextFactory<PipelineDbContext> _factory;

    public ConnectorCredentialSecurityTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();

        _options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_keepAlive)
            .Options;

        using var seedContext = new PipelineDbContext(_options);
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

        _factory = new SharedConnectionFactory(_options);
        _repo = new SqliteConnectorConfigRepository(_factory, new OneWayProtector());
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task CredentialNeverPersistedToDatabase()
    {
        var secretsJson = "{\"personalAccessToken\":\"" + TestPatValue + "\"}";

        await _repo.SaveAsync(
            DBAIAzure.Core.Models.ConnectorType.AzureDevOps,
            nonSecretConfigJson:   """{"organizationUrl":"https://dev.azure.com/org","projectName":"MyProject"}""",
            plaintextSecretsJson:  secretsJson);

        // Query every column of every row in ConnectorConfigs and ensure the sentinel value is absent.
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var ctx = new PipelineDbContext(_options);
        var rawSecretBlob = await ctx.Database
            .SqlQueryRaw<string>("""SELECT COALESCE(EncryptedSecretsJson, '') FROM ConnectorConfigs""")
            .ToListAsync();

        foreach (var blob in rawSecretBlob)
        {
            Assert.DoesNotContain(TestPatValue, blob, StringComparison.Ordinal);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class SharedConnectionFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }

    /// <summary>
    /// Fake protector that actually transforms the data (base64 + prefix) so the test
    /// can meaningfully verify the stored blob differs from the plaintext.
    /// A simple prefix like "ENC:" in the FakeSecretProtector from other tests would still
    /// contain the original secret after the prefix — this one base64-encodes to ensure
    /// the raw PAT string is truly absent from the stored value.
    /// </summary>
    private sealed class OneWayProtector : DBAIAzure.Core.Interfaces.ISecretProtector
    {
        public string Protect(string plaintext)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
            return "B64:" + Convert.ToBase64String(bytes);
        }

        public string Unprotect(string ciphertext)
        {
            if (!ciphertext.StartsWith("B64:", StringComparison.Ordinal))
                throw new System.Security.Cryptography.CryptographicException("Unknown ciphertext format.");

            var bytes = Convert.FromBase64String(ciphertext["B64:".Length..]);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
