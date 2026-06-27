// SQLite-backed persistence for connector configuration with secret encryption (FR-001–FR-007, FR-019).
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// Persists connector settings in the shared SQLite database. Uses a short-lived
/// <c>PipelineDbContext</c> per operation via <c>IDbContextFactory</c> (thread-safe, concurrent-write safe).
/// Secrets are encrypted at rest via <c>ISecretProtector</c> — the plaintext never touches this class's
/// return values or logs (FR-019, Article IX).
/// </summary>
public sealed class SqliteConnectorConfigRepository : IConnectorConfigRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PipelineDbContext> _factory;
    private readonly ISecretProtector _protector;

    /// <summary>All four connector types, used to synthesize the full list returned by GetAllAsync.</summary>
    private static readonly ConnectorType[] AllConnectorTypes =
        [ConnectorType.ServiceNow, ConnectorType.AzureDevOps, ConnectorType.LLM, ConnectorType.Messaging];

    /// <summary>The connector type formerly called "Teams"; tolerated on read and mapped to Messaging (FR-013).</summary>
    private const string LegacyMessagingType = "Teams";

    /// <summary>
    /// Normalizes a persisted connector-type string, mapping the legacy "Teams" value to "Messaging" so an
    /// older database row never fails <see cref="Enum.Parse{ConnectorType}(string)"/> or hides the connector.
    /// </summary>
    private static string NormalizeStoredType(string stored) =>
        stored == LegacyMessagingType ? nameof(ConnectorType.Messaging) : stored;

    public SqliteConnectorConfigRepository(
        IDbContextFactory<PipelineDbContext> factory,
        ISecretProtector protector)
    {
        _factory = factory;
        _protector = protector;
    }

    /// <inheritdoc/>
    public async Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var wanted = type.ToString();
        var record = await db.ConnectorConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.ConnectorType == wanted ||
                     (wanted == nameof(ConnectorType.Messaging) && r.ConnectorType == LegacyMessagingType),
                ct);

        return record is null ? null : MapToConfig(record);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var records = await db.ConnectorConfigs
            .AsNoTracking()
            .ToListAsync(ct);

        var byType = records.ToDictionary(r => NormalizeStoredType(r.ConnectorType));
        return AllConnectorTypes
            .Select(connectorType =>
            {
                if (byType.TryGetValue(connectorType.ToString(), out var record))
                    return MapToConfig(record);

                // Return a sentinel "not configured" placeholder for connectors with no DB row yet.
                return new ConnectorConfig(
                    connectorType,
                    NonSecretConfig: null,
                    HasSecrets: false,
                    IsConfigured: false,
                    LastUpdatedAt: default,
                    LastTestResult: null);
            })
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        ConnectorType type,
        string? nonSecretConfigJson,
        string? plaintextSecretsJson,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.ConnectorConfigs
            .FirstOrDefaultAsync(r => r.ConnectorType == type.ToString(), ct);

        if (existing is null)
        {
            var newRecord = new ConnectorConfigRecord
            {
                ConnectorType = type.ToString(),
                ConfigJson = nonSecretConfigJson,
                EncryptedSecretsJson = plaintextSecretsJson is not null
                    ? _protector.Protect(plaintextSecretsJson)
                    : null,
                IsConfigured = true,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                // FR-017: new save always resets the test result
                LastTestResult = null,
                LastTestMessage = null,
                LastTestedAt = null,
            };
            db.ConnectorConfigs.Add(newRecord);
        }
        else
        {
            existing.ConfigJson = nonSecretConfigJson;
            existing.IsConfigured = true;
            existing.LastUpdatedAt = DateTimeOffset.UtcNow;
            // FR-017: any save resets the test result (field change invalidates prior test)
            existing.LastTestResult = null;
            existing.LastTestMessage = null;
            existing.LastTestedAt = null;

            // Only overwrite the encrypted blob when the operator explicitly provided new secrets.
            // Passing null for plaintextSecretsJson preserves the existing encrypted blob unchanged.
            if (plaintextSecretsJson is not null)
                existing.EncryptedSecretsJson = _protector.Protect(plaintextSecretsJson);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var wanted = type.ToString();
        var record = await db.ConnectorConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.ConnectorType == wanted ||
                     (wanted == nameof(ConnectorType.Messaging) && r.ConnectorType == LegacyMessagingType),
                ct);

        if (record?.EncryptedSecretsJson is null)
            return null;

        try
        {
            return _protector.Unprotect(record.EncryptedSecretsJson);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException)
        {
            // Key ring rotation or tampered payload — callers receive null and show "re-enter credentials".
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task UpdateTestResultAsync(
        ConnectorType type,
        ConnectorTestResult result,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var record = await db.ConnectorConfigs
            .FirstOrDefaultAsync(r => r.ConnectorType == type.ToString(), ct);

        if (record is null)
            return;

        record.LastTestResult  = result.IsSuccess ? "Pass" : "Fail";
        record.LastTestMessage = result.Message;
        record.LastTestedAt    = result.TestedAt;

        await db.SaveChangesAsync(ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────────────────

    private static ConnectorConfig MapToConfig(ConnectorConfigRecord record)
    {
        ConnectorTestResult? lastResult = null;
        if (record.LastTestResult is not null && record.LastTestedAt.HasValue)
        {
            var isSuccess = record.LastTestResult == "Pass";
            lastResult = new ConnectorTestResult(
                Enum.Parse<ConnectorType>(NormalizeStoredType(record.ConnectorType)),
                isSuccess,
                record.LastTestMessage ?? string.Empty,
                record.LastTestedAt.Value);
        }

        return new ConnectorConfig(
            Type: Enum.Parse<ConnectorType>(NormalizeStoredType(record.ConnectorType)),
            NonSecretConfig: record.ConfigJson,
            HasSecrets: record.EncryptedSecretsJson is not null,
            IsConfigured: record.IsConfigured,
            LastUpdatedAt: record.LastUpdatedAt,
            LastTestResult: lastResult);
    }
}
