using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class PendingConnectConfiguration : IEntityTypeConfiguration<PendingConnect>
{
    public void Configure(EntityTypeBuilder<PendingConnect> builder)
    {
        builder.ToTable("pending_connects", Schemas.Accounts);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.SessionId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.ConsumedAt);

        // Callbacks find their sign-in by the session; Dhan's daily limit counts per account and day.
        builder.HasIndex(x => new { x.SessionId, x.CreatedAt });
        builder.HasIndex(x => new { x.AccountId, x.CreatedAt });
    }
}
