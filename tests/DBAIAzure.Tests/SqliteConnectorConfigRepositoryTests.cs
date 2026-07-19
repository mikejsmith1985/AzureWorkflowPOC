// Tests for SqliteConnectorConfigRepository: CRUD, encryption, null-secret preservation, test-result persistence (T012, T030).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for SqliteConnectorConfigRepository using an in-memory SQLite database and a
/// hand-rolled fake ISecretProtector that prefixes ciphertext with "ENC:" so round-trips are
/// verifiable without real Data Protection keys.
/// </summary>
public sealed class SqliteConnectorConfigRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly IDbContextFactory<PipelineDbContext> _factory;
    private readonly FakeSecretProtector _protector;
    private readonly SqliteConnectorConfigRepository _repo;

    public SqliteConnectorConfigRepositoryTests()
    {
        _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
        _keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_keepAliveConnection)
            .Options;

        using var seedContext = new PipelineDbContext(options);
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

        _factory   = new SharedConnectionFactory(options);
        _protector = new FakeSecretProtector();
        _repo      = new SqliteConnectorConfigRepository(_factory, _protector);
    }

    public void Dispose() => _keepAliveConnection.Dispose();

    // ── CRUD round-trip ────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNoRowExists()
    {
        var result = await _repo.GetAsync(ConnectorType.ServiceNow);
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        const string nonSecretJson = """{"instanceUrl":"https://example.service-now.com","username":"svc"}""";
        const string secretJson    = """{"password":"hunter2"}""";

        await _repo.SaveAsync(ConnectorType.ServiceNow, nonSecretJson, secretJson);

        var config = await _repo.GetAsync(ConnectorType.ServiceNow);

        Assert.NotNull(config);
        Assert.True(config.IsConfigured);
        Assert.Equal(nonSecretJson, config.NonSecretConfig);
        Assert.True(config.HasSecrets);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSentinelForUnconfiguredTypes()
    {
        // Only configure one connector — the other three should appear as sentinel "not configured" records.
        await _repo.SaveAsync(ConnectorType.LLM, """{"providerEndpoint":"https://api.anthropic.com","modelName":"claude-sonnet-4-6"}""", """{"apiKey":"sk-ant-test"}""");

        var all = await _repo.GetAllAsync();

        Assert.Equal(4, all.Count);
        Assert.Contains(all, c => c.Type == ConnectorType.LLM && c.IsConfigured);
        Assert.Contains(all, c => c.Type == ConnectorType.ServiceNow  && !c.IsConfigured);
        Assert.Contains(all, c => c.Type == ConnectorType.WorkTracker && !c.IsConfigured);
        Assert.Contains(all, c => c.Type == ConnectorType.Messaging        && !c.IsConfigured);
    }

    // ── Encryption round-trip ──────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_EncryptsSecrets_AndGetDecryptedSecretsAsync_Decrypts()
    {
        const string plaintextSecret = """{"apiKey":"secret-value"}""";
        await _repo.SaveAsync(ConnectorType.LLM, null, plaintextSecret);

        var decrypted = await _repo.GetDecryptedSecretsAsync(ConnectorType.LLM);

        Assert.Equal(plaintextSecret, decrypted);
        // Verify the stored value was actually encrypted (prefixed by fake protector).
        Assert.Equal(1, _protector.ProtectCallCount);
        Assert.Equal(1, _protector.UnprotectCallCount);
    }

    // ── Null-secret preserves existing (T028 / T030) ──────────────────

    [Fact]
    public async Task SaveAsync_WithNullSecret_PreservesExistingEncryptedBlob()
    {
        const string originalSecret = """{"password":"original"}""";
        await _repo.SaveAsync(ConnectorType.ServiceNow, """{"instanceUrl":"https://x","username":"u"}""", originalSecret);

        // Second save — no new secret value (operator left field unchanged).
        await _repo.SaveAsync(ConnectorType.ServiceNow, """{"instanceUrl":"https://x","username":"u2"}""", null);

        var decrypted = await _repo.GetDecryptedSecretsAsync(ConnectorType.ServiceNow);

        Assert.Equal(originalSecret, decrypted);
        // Protect should only have been called once (on the first save).
        Assert.Equal(1, _protector.ProtectCallCount);
    }

    // ── SaveAsync resets test result (FR-017) ─────────────────────────

    [Fact]
    public async Task SaveAsync_AlwaysResetsLastTestResult()
    {
        // Seed a passing test result first.
        await _repo.SaveAsync(ConnectorType.AzureDevOps, """{"organizationUrl":"https://dev.azure.com/o","projectName":"P"}""", """{"personalAccessToken":"pat"}""");
        var testResult = new ConnectorTestResult(ConnectorType.AzureDevOps, true, "Connected.", DateTimeOffset.UtcNow);
        await _repo.UpdateTestResultAsync(ConnectorType.AzureDevOps, testResult);

        var beforeSave = await _repo.GetAsync(ConnectorType.AzureDevOps);
        Assert.NotNull(beforeSave?.LastTestResult);

        // Any save — even without a new secret — must clear the test result.
        await _repo.SaveAsync(ConnectorType.AzureDevOps, """{"organizationUrl":"https://dev.azure.com/o","projectName":"P2"}""", null);

        var afterSave = await _repo.GetAsync(ConnectorType.AzureDevOps);
        Assert.Null(afterSave?.LastTestResult);
    }

    // ── Test-result persistence ────────────────────────────────────────

    [Fact]
    public async Task UpdateTestResultAsync_PersistsResultOnExistingRow()
    {
        await _repo.SaveAsync(ConnectorType.Messaging, null, """{"webhookUrl":"https://webhook.example"}""");

        var testResult = new ConnectorTestResult(ConnectorType.Messaging, false, "Webhook returned 404.", DateTimeOffset.UtcNow);
        await _repo.UpdateTestResultAsync(ConnectorType.Messaging, testResult);

        var config = await _repo.GetAsync(ConnectorType.Messaging);
        Assert.NotNull(config?.LastTestResult);
        Assert.False(config.LastTestResult.IsSuccess);
        Assert.Equal("Webhook returned 404.", config.LastTestResult.Message);
    }

    // ── Concurrent-write produces exactly one row (T011) ───────────────

    [Fact]
    public async Task SaveAsync_TwiceForSameType_ProducesExactlyOneRow()
    {
        const string json = """{"instanceUrl":"https://x","username":"u"}""";

        // Two sequential saves — last write wins; no duplicate rows allowed (unique index).
        await _repo.SaveAsync(ConnectorType.ServiceNow, json, """{"password":"p1"}""");
        await _repo.SaveAsync(ConnectorType.ServiceNow, json, """{"password":"p2"}""");

        // GetAllAsync should still return exactly 4 total entries (one per ConnectorType).
        var all = await _repo.GetAllAsync();
        var snowEntries = all.Where(c => c.Type == ConnectorType.ServiceNow).ToList();
        Assert.Single(snowEntries);
    }

    // ── Other connector rows are unaffected by a save (T030) ──────────

    [Fact]
    public async Task SaveAsync_ForOneConnector_DoesNotAffectOthers()
    {
        await _repo.SaveAsync(ConnectorType.LLM, """{"providerEndpoint":"https://api.x.com","modelName":"m"}""", """{"apiKey":"key1"}""");
        await _repo.SaveAsync(ConnectorType.ServiceNow, """{"instanceUrl":"https://s","username":"u"}""", """{"password":"pw"}""");

        // Update LLM only.
        await _repo.SaveAsync(ConnectorType.LLM, """{"providerEndpoint":"https://api.y.com","modelName":"m2"}""", """{"apiKey":"key2"}""");

        var snowConfig = await _repo.GetAsync(ConnectorType.ServiceNow);
        var decrypted  = await _repo.GetDecryptedSecretsAsync(ConnectorType.ServiceNow);

        Assert.NotNull(snowConfig);
        Assert.Equal("""{"password":"pw"}""", decrypted);
    }

    // ── Legacy "Teams" row is read as the Messaging connector (FR-013) ─

    [Fact]
    public async Task LegacyTeamsRow_IsReadAsMessagingConnector()
    {
        // Simulate a database written before the Teams→Messaging rename by inserting a row whose
        // ConnectorType string is the legacy "Teams" value, then confirm it surfaces as Messaging.
        await using (var seed = _factory.CreateDbContext())
        {
            // JSON values are passed as parameters so their { } braces are not read as format placeholders.
            seed.Database.ExecuteSqlRaw(
                "INSERT INTO ConnectorConfigs " +
                "(ConnectorType, ConfigJson, EncryptedSecretsJson, IsConfigured, LastUpdatedAt) " +
                "VALUES ('Teams', {0}, {1}, 1, '2026-01-01T00:00:00+00:00');",
                """{"platform":"Teams"}""",
                "ENC:" + """{"webhookUrl":"https://w.example"}""");
        }

        var config    = await _repo.GetAsync(ConnectorType.Messaging);
        var decrypted = await _repo.GetDecryptedSecretsAsync(ConnectorType.Messaging);
        var all       = await _repo.GetAllAsync();

        Assert.NotNull(config);
        Assert.Equal(ConnectorType.Messaging, config!.Type);
        Assert.True(config.IsConfigured);
        Assert.Equal("""{"webhookUrl":"https://w.example"}""", decrypted);
        Assert.True(all.Single(c => c.Type == ConnectorType.Messaging).IsConfigured);
    }

    // ── Shared in-memory DbContext factory ────────────────────────────

    private sealed class SharedConnectionFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }

    // ── Hand-rolled ISecretProtector fake ─────────────────────────────

    private sealed class FakeSecretProtector : ISecretProtector
    {
        public int ProtectCallCount   { get; private set; }
        public int UnprotectCallCount { get; private set; }

        public string Protect(string plaintext)
        {
            ProtectCallCount++;
            return "ENC:" + plaintext;
        }

        public string Unprotect(string ciphertext)
        {
            UnprotectCallCount++;
            return ciphertext.StartsWith("ENC:", StringComparison.Ordinal)
                ? ciphertext["ENC:".Length..]
                : throw new System.Security.Cryptography.CryptographicException("Bad ciphertext");
        }
    }
}
