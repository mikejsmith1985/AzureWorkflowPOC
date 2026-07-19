// One-time, idempotent migration of a legacy Azure DevOps connector onto the generic WorkTracker connector (spec-020, FR-015).
using System.Text.Json.Nodes;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Migrations;

/// <summary>
/// Carries an existing Azure DevOps connector row onto the generic <see cref="ConnectorType.WorkTracker"/>
/// connector (provider = Azure DevOps) so an existing deployment keeps working with zero reconfiguration.
/// The encrypted secret is copied verbatim (never decrypted); the run is idempotent — guarded by the
/// presence of a WorkTracker row, so repeated startups are a no-op.
/// </summary>
public static class WorkTrackerConnectorMigration
{
    /// <summary>
    /// Creates the WorkTracker connector from the legacy AzureDevOps row when one exists and no WorkTracker
    /// row is present yet. Returns true when a migration was performed, false when it was a no-op.
    /// </summary>
    public static async Task<bool> MigrateAsync(PipelineDbContext db, CancellationToken ct = default)
    {
        var workTrackerType = nameof(ConnectorType.WorkTracker);
        var azureDevOpsType = nameof(ConnectorType.AzureDevOps);

        if (await db.ConnectorConfigs.AnyAsync(r => r.ConnectorType == workTrackerType, ct))
            return false;

        var legacyAdoRow = await db.ConnectorConfigs
            .FirstOrDefaultAsync(r => r.ConnectorType == azureDevOpsType, ct);
        if (legacyAdoRow is null)
            return false;

        db.ConnectorConfigs.Add(new ConnectorConfigRecord
        {
            ConnectorType        = workTrackerType,
            ConfigJson           = InjectProvider(legacyAdoRow.ConfigJson, nameof(WorkTrackerProvider.AzureDevOps)),
            EncryptedSecretsJson = legacyAdoRow.EncryptedSecretsJson,   // ciphertext copied as-is, never decrypted
            IsConfigured         = legacyAdoRow.IsConfigured,
            LastUpdatedAt        = legacyAdoRow.LastUpdatedAt,
            LastTestResult       = legacyAdoRow.LastTestResult,
            LastTestMessage      = legacyAdoRow.LastTestMessage,
            LastTestedAt         = legacyAdoRow.LastTestedAt,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Adds the <c>provider</c> discriminator to the non-secret JSON, preserving existing fields.</summary>
    private static string InjectProvider(string? configJson, string provider)
    {
        var node = string.IsNullOrWhiteSpace(configJson)
            ? new JsonObject()
            : JsonNode.Parse(configJson) as JsonObject ?? new JsonObject();
        node["provider"] = provider;
        return node.ToJsonString();
    }
}
