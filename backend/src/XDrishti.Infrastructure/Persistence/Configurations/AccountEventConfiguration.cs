using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class AccountEventConfiguration : IEntityTypeConfiguration<AccountEvent>
{
    public void Configure(EntityTypeBuilder<AccountEvent> builder)
    {
        builder.ToTable("account_events", Schemas.Accounts);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.At).IsRequired();
        builder.Property(x => x.AccountId);
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AccountId, x.At });
        builder.Property(x => x.Event).HasColumnName("event").HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(AccountEvent.MaxDetailLength);
        builder.Property(x => x.Actor).HasMaxLength(AccountEvent.MaxActorLength).IsRequired();
    }
}
