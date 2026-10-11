using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class BrokerSecretConfiguration : IEntityTypeConfiguration<BrokerSecretRecord>
{
    public void Configure(EntityTypeBuilder<BrokerSecretRecord> builder)
    {
        builder.ToTable("broker_secrets", Schemas.Accounts);
        builder.HasKey(x => new { x.AccountId, x.Name });
        builder.Property(x => x.Name).HasMaxLength(BrokerSecretNames.MaxLength).IsRequired();
        builder.Property(x => x.Ciphertext).IsRequired();
        builder.Property(x => x.KeyVersion).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        // The secrets go with their account.
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
