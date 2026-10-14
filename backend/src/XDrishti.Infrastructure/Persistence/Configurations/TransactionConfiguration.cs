using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.transactions</c> (ADR 0023, 0025): the trades of the accounts (tradebook files, the owner's hand, the broker's trade history), immutable except for <c>voided_at</c>/<c>voided_by</c>. The runtime role may read, insert and update them, never delete. The identity of a trade (<c>external_id</c>) is unique
/// per account among the trades that are not voided (a partial unique index), so a voided trade can be imported again.
/// </summary>
internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_transactions_side", "side IN ('buy', 'sell')");
            t.HasCheckConstraint("ck_transactions_source", "source IN ('csv', 'manual', 'broker')");
            t.HasCheckConstraint("ck_transactions_qty", "qty > 0");
            t.HasCheckConstraint("ck_transactions_price", "price > 0");
            t.HasCheckConstraint("ck_transactions_charges", "charges >= 0");
            t.HasCheckConstraint("ck_transactions_voided", "(voided_at IS NULL) = (voided_by IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        // A reference by id into the instrument master (another module): no foreign key, the master is refreshed from outside and may retire rows.
        builder.Property(x => x.InstrumentId);
        builder.Property(x => x.Isin).HasMaxLength(Holding.MaxIsinLength);
        builder.Property(x => x.TradingSymbol).HasMaxLength(Holding.MaxSymbolLength).IsRequired();
        builder.Property(x => x.Exchange).HasMaxLength(Holding.MaxExchangeLength).IsRequired();
        builder.Property(x => x.Side).HasConversion(EnumConversions.Lowercase<TradeSide>()).HasMaxLength(4).IsRequired();
        builder.Property(x => x.TradeDate).IsRequired();
        builder.Property(x => x.TradeTime);
        builder.Property(x => x.Qty).IsRequired();
        builder.Property(x => x.Price).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Charges).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Source).HasConversion(EnumConversions.Lowercase<TradeSource>()).HasMaxLength(6).IsRequired();
        builder.Property(x => x.BatchId);

        // The broker history run that read the trade (ADR 0025); null for a trade from a file or typed in.
        builder.Property(x => x.RunId);
        builder.HasOne<BrokerSyncRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TradeBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Seq).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(TradeRules.MaxExternalIdLength).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(Transaction.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.VoidedAt);
        builder.Property(x => x.VoidedBy).HasMaxLength(Transaction.MaxCreatedByLength);
        builder.Ignore(x => x.IsVoided);

        builder.HasIndex(x => new { x.AccountId, x.ExternalId }).IsUnique().HasFilter("voided_at IS NULL").HasDatabaseName("ux_transactions_account_external_active");
        builder.HasIndex(x => new { x.AccountId, x.TradingSymbol }).HasDatabaseName("ix_transactions_account_symbol");
        builder.HasIndex(x => new { x.AccountId, x.Isin }).HasDatabaseName("ix_transactions_account_isin");
        builder.HasIndex(x => x.BatchId).HasDatabaseName("ix_transactions_batch");
        builder.HasIndex(x => x.RunId).HasDatabaseName("ix_transactions_run").HasFilter("run_id IS NOT NULL");
    }
}
