using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<InstrumentRecord>
{
    public void Configure(EntityTypeBuilder<InstrumentRecord> builder)
    {
        builder.ToTable("instruments", Schemas.Market, t =>
            t.HasCheckConstraint("ck_instruments_type", "type IN ('share', 'etf', 'index', 'future')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CanonicalSymbol).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.CanonicalSymbol, "ix_instruments_canonical_symbol").HasDatabaseName("ix_instruments_canonical_symbol").IsUnique();
        builder.Property(x => x.Exchange).HasMaxLength(8).IsRequired();
        builder.Property(x => x.ExchangeSegment).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Isin).HasMaxLength(16);
        builder.Property(x => x.Series).HasMaxLength(8);
        builder.Property(x => x.TickSize).HasPrecision(14, 4);
        builder.Property(x => x.UnderlyingSymbol).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Sector).HasMaxLength(100);
        builder.Property(x => x.SectorSource).HasMaxLength(32);
        builder.Property(x => x.AsmGsmFlag).HasMaxLength(8);
        builder.Property(x => x.AsmGsmCategory).HasMaxLength(16);
        builder.Property(x => x.MtfLeverage).HasPrecision(12, 6);
        builder.Property(x => x.FreezeQuantity).HasColumnName("freeze_qty");

        // Substring search (ILIKE '%text%') on the text columns.
        builder.HasIndex(x => x.CanonicalSymbol, "ix_instruments_canonical_symbol_trgm").HasDatabaseName("ix_instruments_canonical_symbol_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex(x => x.Name, "ix_instruments_name_trgm").HasDatabaseName("ix_instruments_name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex(x => x.DisplayName, "ix_instruments_display_name_trgm").HasDatabaseName("ix_instruments_display_name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex(x => x.Isin, "ix_instruments_isin_trgm").HasDatabaseName("ix_instruments_isin_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");

        builder.HasIndex(x => x.Type);
        builder.HasIndex(x => x.Sector);
        builder.HasIndex(x => x.FnoEligible);
        builder.HasIndex(x => x.RetiredAt);
    }
}
