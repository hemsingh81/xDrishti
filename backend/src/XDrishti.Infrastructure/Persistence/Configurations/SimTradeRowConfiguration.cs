using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.sim_trades</c> (spec P3-10, D3): one row per trade of a stored run, every field a typed column. The columns beyond the four keys are declared from <see cref="TradeColumns"/>, the one list the copy and the reads also use. The checks
/// that hold the closed sets of names and the all-or-nothing costing are generated from the same list. No index beyond the primary key and the unique trade key per run (D3); no cascade (nothing is deleted by the runtime role).
/// </summary>
internal sealed class SimTradeRowConfiguration : IEntityTypeConfiguration<SimTradeRow>
{
    public void Configure(EntityTypeBuilder<SimTradeRow> builder)
    {
        builder.ToTable("sim_trades", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_sim_trades_ordinal", "ordinal >= 0");
            foreach (var column in TradeColumns.Columns.Where(c => c.AllowedNames is not null))
            {
                var names = string.Join(", ", column.AllowedNames!.Select(n => "'" + n + "'"));
                t.HasCheckConstraint($"ck_sim_trades_{column.Name}", column.Nullable ? $"{column.Name} IS NULL OR {column.Name} IN ({names})" : $"{column.Name} IN ({names})");
            }

            t.HasCheckConstraint("ck_sim_trades_costing_all_or_nothing", $"num_nonnulls({string.Join(", ", TradeColumns.CostingNames)}) IN (0, {TradeColumns.CostingCount})");
            t.HasCheckConstraint("ck_sim_trades_costs_charged", "(costs = 'charged') = (cost_quantity IS NOT NULL)");
        });
        builder.HasKey(x => new { x.RunId, x.OpenAtEnd, x.Ordinal }).HasName("pk_sim_trades");
        builder.Property(x => x.RunId).IsRequired();
        builder.Property(x => x.OpenAtEnd).IsRequired();
        builder.Property(x => x.Ordinal).IsRequired();
        builder.Property(x => x.TradeKey).HasMaxLength(160).IsRequired();
        foreach (var column in TradeColumns.Columns)
        {
            builder.Property(column.ClrType, column.Name).HasColumnName(column.Name).HasColumnType(column.SqlType).IsRequired(!column.Nullable);
        }

        builder.HasOne<SimRunRow>().WithMany().HasForeignKey(x => x.RunId).HasConstraintName("fk_sim_trades_run").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RunId, x.TradeKey }).IsUnique().HasDatabaseName("uq_sim_trades_run_trade_key");
    }
}
