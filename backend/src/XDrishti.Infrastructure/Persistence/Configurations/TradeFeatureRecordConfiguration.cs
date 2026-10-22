using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.trade_features</c> (spec P6-04, ADR 0066): the decision-time features of one trade of one run, a side table so that <c>sim_trades</c> and the trade record stay as they are (D2 of P6-00). Insert-only for the runtime role. A plain table, a few thousand
/// rows per run. No foreign key to the run tables: the run is named by its key, like the cell statistics name theirs.
/// </summary>
internal sealed class TradeFeatureRecordConfiguration : IEntityTypeConfiguration<TradeFeatureRecord>
{
    public void Configure(EntityTypeBuilder<TradeFeatureRecord> builder)
    {
        builder.ToTable("trade_features", Schemas.Research, t => t.HasCheckConstraint("ck_trade_features_shape", "run_key ~ '^[0-9a-f]{64}$' AND cardinality(feature_values) > 0"));
        builder.HasKey(x => x.Id).HasName("pk_trade_features");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.RunKey).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.TradeKey).HasColumnType("text").IsRequired();
        builder.Property(x => x.FeatureVersion).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Strategy).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Timeframe).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Side).HasMaxLength(8).IsRequired();
        builder.Property(x => x.TradeDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.FeatureValues).HasColumnType("double precision[]").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.RunKey, x.TradeKey, x.FeatureVersion }).IsUnique().HasDatabaseName("uq_trade_features_run_trade_version");
    }
}
