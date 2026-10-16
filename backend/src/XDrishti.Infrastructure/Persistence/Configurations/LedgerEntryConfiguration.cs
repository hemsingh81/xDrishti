using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.ledger_entries</c> (ADR 0025): the lines of the accounts' ledger at Dhan, immutable and never deleted (the runtime role has no DELETE). The identity of an entry (<c>external_id</c>, a hash) is unique per account, so reading a window twice
/// stores nothing new (a voided entry's identity is free again). <c>description</c> and <c>narration</c> are Dhan's free text, capped, to be shown as plain text only.
/// </summary>
internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    private static readonly ValueConverter<LedgerClass, string> ClassCodes = new(v => LedgerClasses.Code(v), v => ReadClass(v));

    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_ledger_entries_class", "class IN ('funds_in', 'funds_out', 'dividend', 'charges', 'other')");
            t.HasCheckConstraint("ck_ledger_entries_source", "source IN ('broker', 'import')");
            t.HasCheckConstraint("ck_ledger_entries_origin", "(source = 'broker') = (run_id IS NOT NULL) AND (source = 'import') = (batch_id IS NOT NULL)");
            t.HasCheckConstraint("ck_ledger_entries_quantity", "quantity IS NULL OR quantity > 0");
            t.HasCheckConstraint("ck_ledger_entries_voided", "(voided_at IS NULL) = (voided_by IS NULL) AND (voided_at IS NULL OR source = 'import')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.EntryDate).HasColumnName("entry_date").IsRequired();
        builder.Property(x => x.VoucherNo).HasMaxLength(LedgerEntry.MaxVoucherNoLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(LedgerEntry.MaxDescriptionLength).IsRequired();
        builder.Property(x => x.Narration).HasMaxLength(LedgerEntry.MaxNarrationLength).IsRequired();
        builder.Property(x => x.Exchange).HasMaxLength(LedgerEntry.MaxExchangeLength);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.EntryClass).HasColumnName("class").HasConversion(ClassCodes).HasMaxLength(9).IsRequired();
        builder.Property(x => x.RunningBalance).HasPrecision(18, 2);
        builder.Property(x => x.ExternalId).HasMaxLength(LedgerEntry.MaxExternalIdLength).IsRequired();
        builder.Property(x => x.RunId);
        builder.HasOne<BrokerSyncRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.CreatedAt).IsRequired();

        // Where the entry came from: Dhan's ledger, or a report the owner imported (ADR 0029, a depository-charges sheet), which has a batch, the security and the quantity the charge was levied for, and can be voided by undoing its batch.
        builder.Property(x => x.Source).HasConversion(EnumConversions.Lowercase<LedgerSource>()).HasMaxLength(6).IsRequired();
        builder.Property(x => x.BatchId);
        builder.HasOne<CostBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Isin).HasMaxLength(Holding.MaxIsinLength);
        builder.Property(x => x.Quantity);
        builder.Property(x => x.VoidedAt);
        builder.Property(x => x.VoidedBy).HasMaxLength(Transaction.MaxCreatedByLength);
        builder.Ignore(x => x.IsVoided);

        builder.HasIndex(x => new { x.AccountId, x.ExternalId }).IsUnique().HasFilter("voided_at IS NULL").HasDatabaseName("ux_ledger_entries_account_external");
        builder.HasIndex(x => x.BatchId).HasDatabaseName("ix_ledger_entries_batch").HasFilter("batch_id IS NOT NULL");
        builder.HasIndex(x => new { x.AccountId, x.EntryDate }).HasDatabaseName("ix_ledger_entries_account_date");
        builder.HasIndex(x => x.RunId).HasDatabaseName("ix_ledger_entries_run");
    }

    private static LedgerClass ReadClass(string code) => LedgerClasses.TryParse(code, out var value) ? value : LedgerClass.Other;
}
