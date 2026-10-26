using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market.Context;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.context_daily</c> (P3-01d, ADR 0033): one row per India trading day, written once and never edited (the runtime role has no UPDATE or DELETE). The checks are the validation rules of <c>MarketContext</c>, so a stored row can never be refused by it.
/// </summary>
internal sealed class ContextDailyConfiguration : IEntityTypeConfiguration<ContextDailyRecord>
{
    public void Configure(EntityTypeBuilder<ContextDailyRecord> builder)
    {
        builder.ToTable("context_daily", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_context_daily_index", "index_low > 0 AND index_high >= index_low AND index_close >= index_low AND index_close <= index_high");
            t.HasCheckConstraint("ck_context_daily_vix", "vix_close IS NULL OR vix_close > 0");
            t.HasCheckConstraint("ck_context_daily_bars", "index_bars > 0 AND (vix_bars IS NULL OR vix_bars > 0)");
        });
        builder.HasKey(x => x.Date).HasName("pk_context_daily");
        builder.Property(x => x.Date).ValueGeneratedNever();
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
    }
}
