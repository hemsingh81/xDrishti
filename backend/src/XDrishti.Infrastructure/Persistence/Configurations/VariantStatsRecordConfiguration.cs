using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>lab.variant_stats</c> (CF-03, ADR 0205): the per-trade statistics of each variant of a strategy family whose run ended in a result, so the spread of the trial Sharpe ratios can be known. Insert-only for the runtime role; <c>(run_id, variant_hash)</c> is unique,
/// which makes the append idempotent. No foreign key to <c>lab_runs</c> (as <c>test_ledger</c>: a sweep point's run id is derived and has no run row). A figure that is not computable (no spread, too few trades) is null; a stored figure is finite.
/// </summary>
internal sealed class VariantStatsRecordConfiguration : IEntityTypeConfiguration<VariantStatsRecord>
{
    public void Configure(EntityTypeBuilder<VariantStatsRecord> builder)
    {
        builder.ToTable("variant_stats", Schemas.Lab, t =>
        {
            t.HasCheckConstraint("ck_variant_stats_shape", "variant_hash ~ '^[0-9a-f]{64}$' AND n >= 0 AND (sd_r IS NULL OR sd_r >= 0) AND method IN ('full','robust','sweep')");
            t.HasCheckConstraint(
                "ck_variant_stats_finite",
                "(mean_r IS NULL OR (mean_r <> 'NaN'::float8 AND abs(mean_r) <> 'Infinity'::float8)) AND (sd_r IS NULL OR (sd_r <> 'NaN'::float8 AND abs(sd_r) <> 'Infinity'::float8)) AND (skew IS NULL OR (skew <> 'NaN'::float8 AND abs(skew) <> 'Infinity'::float8)) AND (kurt IS NULL OR (kurt <> 'NaN'::float8 AND abs(kurt) <> 'Infinity'::float8)) AND (sr_per_trade IS NULL OR (sr_per_trade <> 'NaN'::float8 AND abs(sr_per_trade) <> 'Infinity'::float8))");
        });
        builder.HasKey(x => x.Id).HasName("pk_variant_stats");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.RunId).IsRequired();
        builder.Property(x => x.VariantHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.N).IsRequired();
        builder.Property(x => x.MeanR);
        builder.Property(x => x.SdR);
        builder.Property(x => x.Skew);
        builder.Property(x => x.Kurt);
        builder.Property(x => x.SrPerTrade);
        builder.Property(x => x.Method).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.RunId, x.VariantHash }).IsUnique().HasDatabaseName("uq_variant_stats_run_variant");
        builder.HasIndex(x => new { x.StrategyId, x.CreatedAt }).HasDatabaseName("ix_variant_stats_strategy_created");
    }
}
