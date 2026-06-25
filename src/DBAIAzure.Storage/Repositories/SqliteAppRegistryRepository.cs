// SQLite-backed persistence for registered repo-apps with lifecycle guards (feature 013).
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// Persists registered apps in the shared SQLite database via a short-lived <c>PipelineDbContext</c>
/// per operation (<c>IDbContextFactory</c> — thread-safe, concurrent-write safe). Enforces
/// registration validation (FR-002), the lifecycle transition table, and a single-in-flight guard so
/// two simultaneous build/run operations cannot run for one app (FR-008, FR-016). Build/run results
/// are stored as JSON; the values handed in are already secret-redacted (FR-009).
/// </summary>
public sealed class SqliteAppRegistryRepository : IAppRegistryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PipelineDbContext> _factory;

    /// <summary>Creates the repository over the shared DbContext factory.</summary>
    public SqliteAppRegistryRepository(IDbContextFactory<PipelineDbContext> factory) => _factory = factory;

    /// <inheritdoc/>
    public async Task<MonitoredApp> RegisterAsync(MonitoredApp app, CancellationToken ct = default)
    {
        // FR-002: reject a missing run command, a non-existent/inaccessible path, and a duplicate name —
        // each with a clear message and no partial row left behind.
        if (string.IsNullOrWhiteSpace(app.Name))
            throw new AppRegistrationException("A name is required.");
        if (string.IsNullOrWhiteSpace(app.RunCommand))
            throw new AppRegistrationException("A run command is required to run the app.");
        if (string.IsNullOrWhiteSpace(app.RepoLocalPath) || !Directory.Exists(app.RepoLocalPath))
            throw new AppRegistrationException($"The repository path '{app.RepoLocalPath}' does not exist or is not accessible.");

        await using var db = await _factory.CreateDbContextAsync(ct);

        if (await db.MonitoredApps.AnyAsync(r => r.OwnerId == app.OwnerId && r.Name == app.Name, ct))
            throw new AppRegistrationException($"An app named '{app.Name}' already exists.");

        db.MonitoredApps.Add(ToEntity(app));
        await db.SaveChangesAsync(ct);
        return app;
    }

    /// <inheritdoc/>
    public async Task<MonitoredApp?> GetAsync(string appId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.MonitoredApps.AsNoTracking().FirstOrDefaultAsync(r => r.AppId == appId, ct);
        return record is null ? null : ToModel(record);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MonitoredApp>> ListByOwnerAsync(string ownerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Materialize first: the SQLite EF provider cannot ORDER BY a DateTimeOffset column, so the
        // newest-first ordering is applied client-side.
        var records = await db.MonitoredApps
            .AsNoTracking()
            .Where(r => r.OwnerId == ownerId)
            .ToListAsync(ct);
        return records
            .OrderByDescending(r => r.CreatedAt)
            .Select(ToModel)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MonitoredApp>> ListMonitoredAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var records = await db.MonitoredApps
            .AsNoTracking()
            .Where(r => r.LinkedWorkflowId != null)
            .ToListAsync(ct);
        return records.Select(ToModel).ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task SetStatusAsync(string appId, AppStatus status, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.MonitoredApps.FirstOrDefaultAsync(r => r.AppId == appId, ct)
            ?? throw new InvalidOperationException($"App '{appId}' was not found.");

        var current = (AppStatus)record.Status;
        if (!IsLegalTransition(current, status))
            throw new InvalidOperationException($"Cannot move app from {current} to {status} (operation already in progress or not allowed).");

        record.Status = (int)status;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task SetBuildResultAsync(string appId, AppBuildResult result, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.MonitoredApps.FirstOrDefaultAsync(r => r.AppId == appId, ct)
            ?? throw new InvalidOperationException($"App '{appId}' was not found.");

        record.Status = (int)(result.Succeeded ? AppStatus.Ready : AppStatus.BuildFailed);
        record.LastBuildResultJson = JsonSerializer.Serialize(result, JsonOptions);
        record.LastBuiltAt = result.At;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task SetRunResultAsync(string appId, AppRunResult result, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.MonitoredApps.FirstOrDefaultAsync(r => r.AppId == appId, ct)
            ?? throw new InvalidOperationException($"App '{appId}' was not found.");

        // A run always returns the app to Ready, regardless of the run outcome (FR-006).
        record.Status = (int)AppStatus.Ready;
        record.LastRunResultJson = JsonSerializer.Serialize(result, JsonOptions);
        record.LastRunAt = result.At;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task SetLinkedWorkflowAsync(string appId, string? workflowId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.MonitoredApps.FirstOrDefaultAsync(r => r.AppId == appId, ct)
            ?? throw new InvalidOperationException($"App '{appId}' was not found.");

        record.LinkedWorkflowId = string.IsNullOrWhiteSpace(workflowId) ? null : workflowId;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string appId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.MonitoredApps.FirstOrDefaultAsync(r => r.AppId == appId, ct);
        if (record is null)
            return;

        db.MonitoredApps.Remove(record);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsByNameAsync(string ownerId, string name, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MonitoredApps.AsNoTracking().AnyAsync(r => r.OwnerId == ownerId && r.Name == name, ct);
    }

    // ── Lifecycle rules ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether a status transition is allowed. A move INTO Building/Running is the single-in-flight
    /// guard (FR-016): it is only legal from a non-busy status, so a second concurrent trigger is
    /// rejected. Build/run RESULT setters bypass this and always resolve a busy app (FR-008).
    /// </summary>
    private static bool IsLegalTransition(AppStatus from, AppStatus to) => (from, to) switch
    {
        (AppStatus.Registered, AppStatus.Building) => true,
        (AppStatus.Ready, AppStatus.Building) => true,
        (AppStatus.BuildFailed, AppStatus.Building) => true,
        (AppStatus.Ready, AppStatus.Running) => true,
        // Allow result setters' explicit terminal resolutions even when called via SetStatusAsync.
        (AppStatus.Building, AppStatus.Ready) => true,
        (AppStatus.Building, AppStatus.BuildFailed) => true,
        (AppStatus.Running, AppStatus.Ready) => true,
        _ => false
    };

    // ── Mapping ──────────────────────────────────────────────────────────────────────────────────

    private MonitoredApp ToModel(MonitoredAppRecord record) => new()
    {
        AppId = record.AppId,
        Name = record.Name,
        OwnerId = record.OwnerId,
        RepoLocalPath = record.RepoLocalPath,
        Branch = record.Branch,
        BuildCommand = record.BuildCommand,
        RunCommand = record.RunCommand,
        Status = (AppStatus)record.Status,
        LastBuildResult = Deserialize<AppBuildResult>(record.LastBuildResultJson),
        LastRunResult = Deserialize<AppRunResult>(record.LastRunResultJson),
        LinkedWorkflowId = record.LinkedWorkflowId,
        LastBuiltAt = record.LastBuiltAt,
        LastRunAt = record.LastRunAt,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };

    private static MonitoredAppRecord ToEntity(MonitoredApp app) => new()
    {
        AppId = app.AppId,
        Name = app.Name,
        OwnerId = app.OwnerId,
        RepoLocalPath = app.RepoLocalPath,
        Branch = app.Branch,
        BuildCommand = app.BuildCommand,
        RunCommand = app.RunCommand,
        Status = (int)app.Status,
        LastBuildResultJson = app.LastBuildResult is null ? null : JsonSerializer.Serialize(app.LastBuildResult, JsonOptions),
        LastRunResultJson = app.LastRunResult is null ? null : JsonSerializer.Serialize(app.LastRunResult, JsonOptions),
        LinkedWorkflowId = app.LinkedWorkflowId,
        LastBuiltAt = app.LastBuiltAt,
        LastRunAt = app.LastRunAt,
        CreatedAt = app.CreatedAt,
        UpdatedAt = app.UpdatedAt
    };

    private static T? Deserialize<T>(string? json) where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<T>(json, JsonOptions);
}
