using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage;

/// <summary>EF Core DbContext for the SQLite pipeline ledger.</summary>
public class PipelineDbContext : DbContext
{
    public PipelineDbContext(DbContextOptions<PipelineDbContext> options) : base(options) { }

    public DbSet<RunRecord>          Runs      { get; set; } = null!;
    public DbSet<StepSnapshotRecord> Snapshots { get; set; } = null!;

    /// <summary>Phase-handler run audit records — independent of the ticket pipeline (FR-017).</summary>
    public DbSet<PhaseRunRecord>     PhaseRuns { get; set; } = null!;

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
    }
}
