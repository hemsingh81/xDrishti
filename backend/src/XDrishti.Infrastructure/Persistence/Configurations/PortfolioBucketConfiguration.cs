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
        builder.Property(x => x.Name).HasMaxLength(PortfolioBucket.MaxNameLength).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique(); // exact spelling: target of the accounts' default-bucket reference; the case-insensitive rule is a second index in the migration
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
