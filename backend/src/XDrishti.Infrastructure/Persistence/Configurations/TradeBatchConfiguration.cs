using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.trade_batches</c> (ADR 0023): one row per import of a tradebook file that added trades — the file's name and SHA-256 (never the file), the counts, who and when, and whether it was undone. Never deleted: the runtime role has no DELETE.
/// </summary>
internal sealed class TradeBatchConfiguration : IEntityTypeConfiguration<TradeBatch>
{
    public void Configure(EntityTypeBuilder<TradeBatch> builder)
    {
        builder.ToTable("trade_batches", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_trade_batches_status", "status IN ('imported', 'undone')");
            t.HasCheckConstraint("ck_trade_batches_counts", "parsed >= 0 AND imported >= 0 AND duplicates >= 0 AND skipped >= 0 AND bad >= 0");
            t.HasCheckConstraint("ck_trade_batches_undone", "(status = 'undone') = (undone_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.FileName).HasMaxLength(TradeBatch.MaxFileNameLength).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(TradeBatch.Sha256Length).IsRequired();
        builder.Property(x => x.Parsed).IsRequired();
        builder.Property(x => x.Imported).IsRequired();
        builder.Property(x => x.Duplicates).IsRequired();
        builder.Property(x => x.Skipped).IsRequired();
        builder.Property(x => x.Bad).IsRequired();
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<TradeBatchStatus>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(Transaction.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UndoneAt);

        builder.HasIndex(x => new { x.AccountId, x.CreatedAt }).HasDatabaseName("ix_trade_batches_account_created");
    }
}
