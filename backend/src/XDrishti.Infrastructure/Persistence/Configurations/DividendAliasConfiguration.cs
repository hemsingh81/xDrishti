using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.dividend_aliases</c> (ADR 0028): what the owner decided a scrip name of a dividend file means for an account — the normalised name points to a holding, to an instrument of the master list, or to nothing. One alias per account and name; it is
/// pointed elsewhere by an update and never deleted (the runtime role has no DELETE). An alias only guides later files; dividends already imported are never changed by it.
/// </summary>
internal sealed class DividendAliasConfiguration : IEntityTypeConfiguration<DividendAlias>
{
    public void Configure(EntityTypeBuilder<DividendAlias> builder)
    {
        builder.ToTable("dividend_aliases", Schemas.Portfolio, t =>
            t.HasCheckConstraint("ck_dividend_aliases_target", "holding_id IS NULL OR instrument_id IS NULL"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.NameKey).HasMaxLength(DividendAlias.MaxNameKeyLength).IsRequired();
        builder.Property(x => x.HoldingId);
        builder.HasOne<Holding>().WithMany().HasForeignKey(x => x.HoldingId).OnDelete(DeleteBehavior.Restrict);

        // A reference by id into the instrument master (another module): no foreign key, the master is refreshed from outside and may retire rows.
        builder.Property(x => x.InstrumentId);
        builder.Property(x => x.CreatedBy).HasMaxLength(Dividend.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Ignore(x => x.Target);

        // Optimistic concurrency: PostgreSQL's system column xmin changes with every update of the row. An alias pointed elsewhere by two imports at once cannot lose one of the updates.
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(x => new { x.AccountId, x.NameKey }).IsUnique().HasDatabaseName("ux_dividend_aliases_account_name");
        builder.HasIndex(x => x.HoldingId).HasDatabaseName("ix_dividend_aliases_holding").HasFilter("holding_id IS NOT NULL");
    }
}
