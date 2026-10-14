using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.dividend_batches</c> (ADR 0028): one row per import of a dividend file that added dividends — the file's name and SHA-256 (never the file), the counts and rupees, who and when, and whether it was undone. Never deleted: the runtime role has no DELETE.
/// </summary>
internal sealed class DividendBatchConfiguration : IEntityTypeConfiguration<DividendBatch>
{
    public void Configure(EntityTypeBuilder<DividendBatch> builder)
    {
        builder.ToTable("dividend_batches", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_dividend_batches_status", "status IN ('imported', 'undone')");
            t.HasCheckConstraint("ck_dividend_batches_counts", "parsed >= 0 AND imported >= 0 AND duplicates >= 0 AND bad >= 0 AND amount >= 0");
            t.HasCheckConstraint("ck_dividend_batches_undone", "(status = 'undone') = (undone_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.FileName).HasMaxLength(DividendBatch.MaxFileNameLength).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(DividendBatch.Sha256Length).IsRequired();
        builder.Property(x => x.Parsed).IsRequired();
        builder.Property(x => x.Imported).IsRequired();
        builder.Property(x => x.Duplicates).IsRequired();
        builder.Property(x => x.Bad).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<DividendBatchStatus>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(Dividend.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UndoneAt);

        // Optimistic concurrency: PostgreSQL's system column xmin changes with every update of the row. Two undos of one batch cannot both mark it (the second matches no row, is run again and finds the batch undone),
        // and an alias pointed elsewhere by two imports at once cannot lose one of the updates.
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(x => new { x.AccountId, x.CreatedAt }).HasDatabaseName("ix_dividend_batches_account_created");
    }
}
