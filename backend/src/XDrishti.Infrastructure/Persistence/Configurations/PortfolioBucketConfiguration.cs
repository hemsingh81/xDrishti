using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class PortfolioBucketConfiguration : IEntityTypeConfiguration<PortfolioBucket>
{
    public void Configure(EntityTypeBuilder<PortfolioBucket> builder)
    {
        builder.ToTable("portfolio_buckets", Schemas.Accounts);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(PortfolioBucket.MaxStoredNameLength).IsRequired();
        // Names are unique ignoring case: the index ux_portfolio_buckets_name_lower in the migration (EF Core cannot model expression indexes).
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ArchivedAt);

        // One default bucket per account: the unique link is what makes creating it safe against two requests at once (NULL for custom buckets, which may repeat).
        builder.Property(x => x.DefaultForAccountId);
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.DefaultForAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DefaultForAccountId).IsUnique().HasDatabaseName("ux_portfolio_buckets_default_for_account");
    }
}
