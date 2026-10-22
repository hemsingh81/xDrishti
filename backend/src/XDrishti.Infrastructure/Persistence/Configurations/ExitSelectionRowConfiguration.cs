using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.exit_selections</c> (spec P4-06, D10): one row per stored exit choice for a cell. The selection key is unique and is the idempotency key; the comparison it was made from is a foreign key without cascade (nothing is deleted by the
/// runtime role). The record is <c>text</c> for the reason of ADR 0045 D1. Insert-only for the runtime role.
/// </summary>
internal sealed class ExitSelectionRowConfiguration : IEntityTypeConfiguration<ExitSelectionRow>
{
    public void Configure(EntityTypeBuilder<ExitSelectionRow> builder)
    {
        builder.ToTable("exit_selections", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_exit_selections_keys", "selection_key ~ '^[0-9a-f]{64}$' AND record_sha256 ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(x => x.Id).HasName("pk_exit_selections");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.SelectionKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.ComparisonId).IsRequired();
        builder.Property(x => x.StrategyId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Timeframe).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Side).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Style).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Regime).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ChosenCandidateId).HasMaxLength(200);
        builder.Property(x => x.RecordJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.RecordSha256).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.SelectionKey).IsUnique().HasDatabaseName("uq_exit_selections_selection_key");
        builder.HasIndex(x => x.ComparisonId).HasDatabaseName("ix_exit_selections_comparison_id");
        builder.HasOne<SimComparisonRow>().WithMany().HasForeignKey(x => x.ComparisonId).HasConstraintName("fk_exit_selections_comparison").OnDelete(DeleteBehavior.Restrict);
    }
}
