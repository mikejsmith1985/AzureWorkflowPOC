using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Storage;

/// <summary>EF Core DbContext for the SQLite pipeline ledger.</summary>
public class PipelineDbContext : DbContext
{
    public PipelineDbContext(DbContextOptions<PipelineDbContext> options) : base(options) { }

    public DbSet<RunRecord>          Runs      { get; set; } = null!;
    public DbSet<StepSnapshotRecord> Snapshots { get; set; } = null!;

    /// <summary>Phase-handler run audit records — independent of the ticket pipeline (FR-017).</summary>
    public DbSet<PhaseRunRecord>     PhaseRuns { get; set; } = null!;

    /// <summary>One configuration row per connector type; secrets stored encrypted (FR-019).</summary>
    public DbSet<ConnectorConfigRecord> ConnectorConfigs { get; set; } = null!;

    /// <summary>One row per saved workflow; nodes, edges, settings, and chat history stored as JSON blobs.</summary>
    public DbSet<WorkflowDefinitionRecord> Workflows { get; set; } = null!;

    /// <summary>One row per workflow builder run; updated on every status transition (FR-18).</summary>
    public DbSet<WorkflowRunEntity> WorkflowBuilderRuns { get; set; } = null!;

    /// <summary>Step-level audit events produced by IWorkflowObserver (FR-21).</summary>
    public DbSet<WorkflowExecutionEventEntity> WorkflowExecutionEvents { get; set; } = null!;

    /// <summary>One row per registered repo-app (feature 013); build/run results stored as JSON.</summary>
    public DbSet<MonitoredAppRecord> MonitoredApps { get; set; } = null!;

    /// <summary>One heartbeat row per monitored app (feature 013).</summary>
    public DbSet<AppMonitoringHeartbeatRecord> AppMonitoringHeartbeats { get; set; } = null!;

    /// <summary>Close-the-loop dedup signatures per monitored app (feature 013).</summary>
    public DbSet<AppRaisedIssueRecord> AppRaisedIssues { get; set; } = null!;

    /// <summary>Append-only AI-cost ledger — runtime + development spend per binding key (spec-017).</summary>
    public DbSet<CostLedgerEntryEntity> CostLedgerEntries { get; set; } = null!;

    /// <summary>binding key → work item map for dev-usage resolution (spec-017, C1).</summary>
    public DbSet<BindingWorkItemMapEntity> BindingWorkItemMap { get; set; } = null!;

    /// <summary>MAF workflow checkpoints — durable HITL pause/resume across restart (spec-019 T030).</summary>
    public DbSet<WorkflowCheckpointEntity> WorkflowCheckpoints { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RunRecord>(entity =>
        {
            entity.HasKey(r => r.RunId);
            entity.Property(r => r.RunId).ValueGeneratedNever();
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.StartedAt);
            entity.HasIndex(r => r.Source);
            entity.HasMany(r => r.Snapshots)
                  .WithOne()
                  .HasForeignKey(s => s.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StepSnapshotRecord>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedOnAdd();
            entity.HasIndex(s => s.RunId);
        });

        modelBuilder.Entity<PhaseRunRecord>(entity =>
        {
            entity.HasKey(r => r.RunId);
            entity.Property(r => r.RunId).ValueGeneratedNever();
            entity.HasIndex(r => r.FeatureKey);
            entity.HasIndex(r => r.Phase);
            entity.HasIndex(r => r.Status);
            // One record per (feature, phase) — enforces single-record-per-phase and backs
            // idempotent upsert (data-model.md / FR-013).
            entity.HasIndex(r => new { r.FeatureKey, r.Phase }).IsUnique();
        });

        modelBuilder.Entity<ConnectorConfigRecord>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedOnAdd();
            // One record per connector type — backs the concurrency-safe upsert in the repository.
            entity.HasIndex(r => r.ConnectorType).IsUnique();
        });

        modelBuilder.Entity<WorkflowRunEntity>(entity =>
        {
            entity.ToTable("WorkflowBuilderRuns");
            entity.HasKey(r => r.RunId);
            entity.Property(r => r.RunId).ValueGeneratedNever();
            entity.HasIndex(r => r.WorkflowId);
            entity.HasIndex(r => r.Status);
            entity.HasMany(r => r.ExecutionEvents)
                  .WithOne()
                  .HasForeignKey(e => e.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowExecutionEventEntity>(entity =>
        {
            entity.ToTable("WorkflowExecutionEvents");
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).ValueGeneratedNever();
            entity.HasIndex(e => e.RunId);
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => new { e.RunId, e.OccurredAt });
        });

        modelBuilder.Entity<WorkflowDefinitionRecord>(entity =>
        {
            // Map to "WorkflowDefinitions" — the name used by the idempotent raw-SQL migration
            // in Program.cs. Without this, EF Core defaults to the DbSet property name "Workflows",
            // which differs from the table created for databases that pre-date the Workflow Builder.
            entity.ToTable("WorkflowDefinitions");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            // Enforces the personal-gallery uniqueness rule: each user can have only one workflow with a given name.
            entity.HasIndex(r => new { r.OwnerId, r.Name }).IsUnique();
            // Covering index for the ListByOwner query — avoids a full table scan per user.
            entity.HasIndex(r => r.OwnerId);
        });

        modelBuilder.Entity<MonitoredAppRecord>(entity =>
        {
            entity.ToTable("MonitoredApps");
            entity.HasKey(r => r.AppId);
            entity.Property(r => r.AppId).ValueGeneratedNever();
            // Each owner can have only one app with a given name (data-model: per-owner uniqueness).
            entity.HasIndex(r => new { r.OwnerId, r.Name }).IsUnique();
            // Covering index for the per-owner gallery query.
            entity.HasIndex(r => r.OwnerId);
        });

        modelBuilder.Entity<AppMonitoringHeartbeatRecord>(entity =>
        {
            entity.ToTable("AppMonitoringHeartbeats");
            entity.HasKey(r => r.AppId);
            entity.Property(r => r.AppId).ValueGeneratedNever();
        });

        modelBuilder.Entity<AppRaisedIssueRecord>(entity =>
        {
            entity.ToTable("AppRaisedIssues");
            entity.HasKey(r => r.Signature);
            entity.Property(r => r.Signature).ValueGeneratedNever();
            entity.HasIndex(r => r.AppId);
        });

        modelBuilder.Entity<CostLedgerEntryEntity>(entity =>
        {
            entity.ToTable("CostLedgerEntries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            // Totals are summed by binding key + dimension — index both.
            entity.HasIndex(e => new { e.BindingKey, e.Dimension });
        });

        modelBuilder.Entity<BindingWorkItemMapEntity>(entity =>
        {
            entity.ToTable("BindingWorkItemMap");
            entity.HasKey(e => e.BindingKey);
            entity.Property(e => e.BindingKey).ValueGeneratedNever();
        });

        modelBuilder.Entity<WorkflowCheckpointEntity>(entity =>
        {
            entity.ToTable("WorkflowCheckpoints");
            // A checkpoint is uniquely identified by its session + id within the checkpoint tree.
            entity.HasKey(e => new { e.SessionId, e.CheckpointId });
            // The index query fetches a session's checkpoints filtered by parent, in write order.
            entity.HasIndex(e => new { e.SessionId, e.ParentCheckpointId });
        });
    }
}
