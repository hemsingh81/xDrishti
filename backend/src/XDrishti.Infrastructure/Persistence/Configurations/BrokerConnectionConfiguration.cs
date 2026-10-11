using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class BrokerConnectionConfiguration : IEntityTypeConfiguration<BrokerConnection>
{
    public void Configure(EntityTypeBuilder<BrokerConnection> builder)
    {
        builder.ToTable("broker_connections", Schemas.Accounts, t =>
            // Expired is derived when read (expires_at), never stored.
            t.HasCheckConstraint("ck_broker_connections_status", "status IN ('NotConnected', 'Connected')"));
        builder.HasKey(x => x.AccountId);
        builder.Property(x => x.AccountId).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ExpiresAt);
        builder.Property(x => x.BrokerUserId).HasMaxLength(BrokerConnection.MaxBrokerUserIdLength);
        builder.Property(x => x.BrokerUserName).HasMaxLength(BrokerConnection.MaxBrokerUserNameLength);
        builder.Property(x => x.LastConnectedAt);
        builder.Property(x => x.LastError).HasMaxLength(BrokerConnection.MaxErrorLength);

        // A sign-in and a disconnect of the same account must not overwrite each other.
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasOne<BrokerAccount>().WithOne().HasForeignKey<BrokerConnection>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
