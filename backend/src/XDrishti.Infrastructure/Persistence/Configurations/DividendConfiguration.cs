using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.dividends</c> (ADR 0026): the dividends the owner entered, immutable except for <c>voided_at</c>/<c>voided_by</c>. The runtime role may read, insert and update them, never delete. <c>holding_id</c> is null for a dividend not allocated to a holding.
/// <c>note</c> is the owner's free text (plain text, capped). A dividend read from a file (<c>source</c> csv, ADR 0028) has the <c>batch_id</c> of its import, an <c>external_id</c> (its identity, unique per account among the dividends that are not voided — a
/// partial unique index, so a voided dividend can be imported again), the file's <c>quantity</c> and, when it belongs to no holding, the file's <c>scrip_name</c>.
/// </summary>
internal sealed class DividendConfiguration : IEntityTypeConfiguration<Dividend>
{
    public void Configure(EntityTypeBuilder<Dividend> builder)
    {
        builder.ToTable("dividends", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_dividends_amount", "amount > 0");
            t.HasCheckConstraint("ck_dividends_per_share", "per_share IS NULL OR per_share > 0");
            t.HasCheckConstraint("ck_dividends_source", "source IN ('manual', 'csv')");
            t.HasCheckConstraint("ck_dividends_quantity", "quantity IS NULL OR quantity > 0");
            t.HasCheckConstraint("ck_dividends_file", "(source = 'csv') = (external_id IS NOT NULL AND batch_id IS NOT NULL)");
            t.HasCheckConstraint("ck_dividends_voided", "(voided_at IS NULL) = (voided_by IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.HoldingId);
        builder.HasOne<Holding>().WithMany().HasForeignKey(x => x.HoldingId).OnDelete(DeleteBehavior.Restrict);

        // A reference by id into the instrument master (another module): no foreign key, the master is refreshed from outside and may retire rows.
        builder.Property(x => x.InstrumentId);
        builder.Property(x => x.Isin).HasMaxLength(Holding.MaxIsinLength);
        builder.Property(x => x.TradingSymbol).HasMaxLength(Holding.MaxSymbolLength);
        builder.Property(x => x.PayDate).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PerShare).HasPrecision(18, 4);
        builder.Property(x => x.Note).HasMaxLength(DividendRules.MaxNoteLength);
        builder.Property(x => x.Source).HasConversion(EnumConversions.Lowercase<DividendSource>()).HasMaxLength(6).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(Dividend.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.VoidedAt);
        builder.Property(x => x.VoidedBy).HasMaxLength(Dividend.MaxCreatedByLength);
        builder.Property(x => x.BatchId);
        builder.HasOne<DividendBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ExternalId).HasMaxLength(DividendRules.MaxExternalIdLength);
        builder.Property(x => x.Quantity);
        builder.Property(x => x.ScripName).HasMaxLength(DividendRules.MaxScripNameLength);
        builder.Ignore(x => x.IsVoided);

        builder.HasIndex(x => new { x.AccountId, x.PayDate }).HasDatabaseName("ix_dividends_account_pay_date");
        builder.HasIndex(x => new { x.AccountId, x.ExternalId }).IsUnique().HasFilter("voided_at IS NULL AND external_id IS NOT NULL").HasDatabaseName("ux_dividends_account_external_active");
        builder.HasIndex(x => x.BatchId).HasDatabaseName("ix_dividends_batch").HasFilter("batch_id IS NOT NULL");
        builder.HasIndex(x => new { x.HoldingId, x.PayDate }).HasDatabaseName("ix_dividends_holding_pay_date").HasFilter("holding_id IS NOT NULL");
    }
}
