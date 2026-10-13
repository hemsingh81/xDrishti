using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable("holdings", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_holdings_status", "status IN ('open', 'closed')");
            t.HasCheckConstraint("ck_holdings_qty", "qty >= 0");
            t.HasCheckConstraint("ck_holdings_last_price_source", "last_price_source IN ('sync', 'quote')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.BucketId).IsRequired();
        builder.HasOne<PortfolioBucket>().WithMany().HasForeignKey(x => x.BucketId).OnDelete(DeleteBehavior.Restrict);

        // A reference by id into the instrument master (another module): no foreign key, the master is refreshed from outside and may retire rows.
        builder.Property(x => x.InstrumentId);
        builder.Property(x => x.Exchange).HasMaxLength(Holding.MaxExchangeLength).IsRequired();
        builder.Property(x => x.SecurityId).HasMaxLength(Holding.MaxSecurityIdLength).IsRequired();
        builder.Property(x => x.Isin).HasMaxLength(Holding.MaxIsinLength);
        builder.Property(x => x.TradingSymbol).HasMaxLength(Holding.MaxSymbolLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(Holding.MaxNameLength).IsRequired();
        builder.Property(x => x.Qty).IsRequired();
        builder.Property(x => x.AvgCost).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.LastPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.LastPriceAt);

        // The latest quote only (ADR 0019): the previous session's close and where the last price came from.
        builder.Property(x => x.PreviousClose).HasPrecision(18, 4);
        builder.Property(x => x.LastPriceSource).HasConversion(EnumConversions.Lowercase<PriceSource>()).HasMaxLength(8).IsRequired().HasDefaultValue(PriceSource.Sync);
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<HoldingStatus>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.FirstSeenAt).IsRequired();
        builder.Property(x => x.ClosedAt);
        builder.Property(x => x.SyncedAt).IsRequired();

        // The broker lists an instrument once per account; a holding that comes back after being closed is the same row.
        builder.HasIndex(x => new { x.AccountId, x.SecurityId, x.Exchange }).IsUnique().HasDatabaseName("ux_holdings_account_security");
        builder.HasIndex(x => new { x.AccountId, x.Status }).HasDatabaseName("ix_holdings_account_status");
        builder.HasIndex(x => x.BucketId);
        builder.HasIndex(x => x.InstrumentId);

        // Optimistic concurrency: PostgreSQL's system column xmin changes with every update of the row. A sync that read a holding before a quote was written into it cannot save its
        // (older) view over the quote: the save matches nothing and is refused, and the sync reads again (PortfolioUnitOfWork.RunAsync).
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasMany(x => x.Lots).WithOne().HasForeignKey(x => x.HoldingId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lots).HasField("_lots").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
