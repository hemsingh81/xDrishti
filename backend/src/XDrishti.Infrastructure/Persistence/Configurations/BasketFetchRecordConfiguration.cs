using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Market.SystemBaskets;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class BasketFetchRecordConfiguration : IEntityTypeConfiguration<BasketFetchRecord>
{
    public void Configure(EntityTypeBuilder<BasketFetchRecord> builder)
    {
        builder.ToTable("basket_fetches", Schemas.Market, t =>
            t.HasCheckConstraint("ck_basket_fetches_outcome", "outcome IN ('unchanged', 'applied', 'rejected', 'failed', 'skipped')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Etag).HasMaxLength(200);
        builder.Property(x => x.LastModified).HasMaxLength(100);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.Outcome).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Warnings).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Error).HasMaxLength(200);
        builder.Property(x => x.Actor).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SettingsSnapshot).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.BasketId, x.FetchedAt });
        builder.HasOne<Basket>().WithMany().HasForeignKey(x => x.BasketId).OnDelete(DeleteBehavior.Restrict);
    }
}
