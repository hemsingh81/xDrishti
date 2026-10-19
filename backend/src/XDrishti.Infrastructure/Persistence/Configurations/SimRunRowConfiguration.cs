using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.sim_runs</c> (spec P3-10, D2): one row per stored simulation run, the facts a query filters by as typed columns and the whole record without its trades as text. The run key is unique and is the idempotency key. Insert-only
/// for the runtime role (the migration revokes UPDATE and DELETE).
/// </summary>
internal sealed class SimRunRowConfiguration : IEntityTypeConfiguration<SimRunRow>
{
    public void Configure(EntityTypeBuilder<SimRunRow> builder)
    {
        var kinds = string.Join(", ", EnumText<UniverseKind>.Names.Select(n => "'" + n + "'"));
        builder.ToTable("sim_runs", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_sim_runs_run_key", "run_key ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_sim_runs_costs", "(costs_sheet_id IS NULL) = (costs_rates_sha256 IS NULL)");
            t.HasCheckConstraint("ck_sim_runs_universe_kind", $"universe_kind IN ({kinds})");
            t.HasCheckConstraint("ck_sim_runs_counts", "trade_count >= 0 AND open_at_end_count >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_sim_runs");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.RunKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.EngineVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RecordSchemaVersion).IsRequired();
        builder.Property(x => x.TradeSchemaVersion).IsRequired();
        builder.Property(x => x.StrategyId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DefinitionSha256).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.FromDate).IsRequired();
        builder.Property(x => x.ToDate).IsRequired();
        builder.Property(x => x.UniverseKind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.UniverseLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Instruments).IsRequired();
        builder.Property(x => x.InstrumentsRun).IsRequired();
        builder.Property(x => x.TradesInWindow).IsRequired();
        builder.Property(x => x.CandidatesInWindow).IsRequired();
        builder.Property(x => x.TradeCount).IsRequired();
        builder.Property(x => x.OpenAtEndCount).IsRequired();
        builder.Property(x => x.AcknowledgeCsvAdjusted).IsRequired();
        builder.Property(x => x.Fills).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CostsSheetId).HasMaxLength(64);
        builder.Property(x => x.CostsRatesSha256).HasColumnType("character(64)");
        builder.Property(x => x.RecordJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.RecordSha256).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.TradesSha256).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.RunKey).IsUnique().HasDatabaseName("uq_sim_runs_run_key");
        builder.HasIndex(x => new { x.StrategyId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_sim_runs_strategy_created");
    }
}
