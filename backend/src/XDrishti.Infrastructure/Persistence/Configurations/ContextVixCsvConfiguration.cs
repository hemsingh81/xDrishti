using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market.Context;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>market.context_vix_csv</c> (P3-01d fallback D, ADR 0290): INDIA VIX closes from owner-dropped NSE CSVs, one per day, insert-only for the runtime role.</summary>
internal sealed class ContextVixCsvConfiguration : IEntityTypeConfiguration<ContextVixCsvRecord>
{
    public void Configure(EntityTypeBuilder<ContextVixCsvRecord> builder)
    {
        builder.ToTable("context_vix_csv", Schemas.Market, t => t.HasCheckConstraint("ck_context_vix_csv_close", "vix_close > 0"));
        builder.HasKey(x => x.Date).HasName("pk_context_vix_csv");
        builder.Property(x => x.Date).ValueGeneratedNever();
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
    }
}
