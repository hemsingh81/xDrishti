using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Live;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.live_quotes</c>: one row per instrument, <c>UNLOGGED</c> (no write-ahead log: it is rewritten every few seconds and emptied every morning, and losing it in a crash costs nothing). An unlogged table may reference a logged
/// one, so the instrument list is referenced. The runtime role may read, insert and update; clearing it is the function <c>market.clear_live_quotes()</c>, because the role has no DELETE or TRUNCATE.
/// </summary>
internal sealed class LiveQuoteConfiguration : IEntityTypeConfiguration<LiveQuoteRecord>
{
    public void Configure(EntityTypeBuilder<LiveQuoteRecord> builder)
    {
        builder.ToTable("live_quotes", Schemas.Market, t => t.HasCheckConstraint("ck_live_quotes_last_price", "last_price > 0"));
        builder.IsUnlogged();
        builder.HasKey(x => x.InstrumentId);
        builder.Property(x => x.InstrumentId).ValueGeneratedNever();
        builder.HasOne<InstrumentRecord>().WithOne().HasForeignKey<LiveQuoteRecord>(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.LastPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.TickAt).IsRequired();
    }
}
