using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Auth;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class OwnerAccountConfiguration : IEntityTypeConfiguration<OwnerAccount>
{
    public void Configure(EntityTypeBuilder<OwnerAccount> builder)
    {
        builder.ToTable("owner_accounts", Schemas.Auth);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.FailedAttempts).IsRequired();
        builder.Property(x => x.LockedUntil);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.PasswordChangedAt).IsRequired();

        // Optimistic concurrency: PostgreSQL's system column xmin changes with every update of the row. Concurrent
        // login attempts that count failures can therefore never overwrite each other's counter (see LoginHandler).
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}
