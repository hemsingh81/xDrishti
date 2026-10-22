using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>lab.lab_runs</c> (spec P5-02, ADR 0058): a Lab run, which is also the queue. The identity columns are written once; the runtime role may update only the state columns and never delete (the migration grants by column). A partial unique
/// index on the key where the state is queued, running or succeeded makes a second post of the same input the same run, whoever races; failed, cancelled and interrupted runs leave the key free. The simulation runs it produced are named by
/// key in <c>sim_run_keys</c> (a soft reference: no foreign key across schemas).
/// </summary>
internal sealed class LabRunRowConfiguration : IEntityTypeConfiguration<LabRunRow>
{
    public void Configure(EntityTypeBuilder<LabRunRow> builder)
    {
        builder.ToTable("lab_runs", Schemas.Lab, t =>
        {
            t.HasCheckConstraint("ck_lab_runs_state", "state IN ('queued', 'running', 'succeeded', 'failed', 'cancelled', 'interrupted')");
            t.HasCheckConstraint("ck_lab_runs_key", "key ~ '^[0-9a-f]{64}$' AND strategy_hash ~ '^[0-9a-f]{64}$' AND config_hash ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_lab_runs_window", "window_from <= window_to");
            t.HasCheckConstraint("ck_lab_runs_progress", "progress_done >= 0 AND progress_total >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_lab_runs");
        builder.Property(x => x.Key).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.StrategyHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Universe).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.WindowFrom).HasColumnType("date").IsRequired();
        builder.Property(x => x.WindowTo).HasColumnType("date").IsRequired();
        builder.Property(x => x.Method).HasColumnType("text").IsRequired();
        builder.Property(x => x.ConfigHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.EngineVersion).HasColumnType("text").IsRequired();
        builder.Property(x => x.State).HasColumnType("text").IsRequired();
        builder.Property(x => x.Stage).HasColumnType("text");
        builder.Property(x => x.SimRunKeys).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.Counts).HasColumnType("jsonb");
        builder.Property(x => x.Problems).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Stages).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Labels).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Error).HasColumnType("text");
        builder.Property(x => x.Host).HasColumnType("text");
        builder.Property(x => x.ProgressDone).HasDefaultValue(0);
        builder.Property(x => x.ProgressTotal).HasDefaultValue(0);
        builder.Property(x => x.CancelRequested).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.StrategyId, x.CreatedAt }).HasDatabaseName("ix_lab_runs_strategy_created");
        builder.HasIndex(x => x.Key).IsUnique().HasFilter("state IN ('queued', 'running', 'succeeded')").HasDatabaseName("uq_lab_runs_active_key");
        builder.HasIndex(x => x.CreatedAt).HasFilter("state = 'queued'").HasDatabaseName("ix_lab_runs_queued");
        builder.HasOne<StrategyVersionRow>().WithMany().HasForeignKey(x => x.StrategyHash).HasPrincipalKey(x => x.Hash).HasConstraintName("fk_lab_runs_strategy_version").OnDelete(DeleteBehavior.Restrict);
    }
}
