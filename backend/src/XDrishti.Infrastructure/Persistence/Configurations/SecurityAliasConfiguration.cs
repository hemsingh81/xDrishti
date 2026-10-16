using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.security_aliases</c> (ADR 0029): the security names that a demat holding statement, a depository-charges sheet or the owner's choice taught the application belong to an ISIN, per account - the normalised name (<c>name_key</c>) with the ISIN and the name as the
/// file wrote it. A name may lead to more than one ISIN; one row per account, name and ISIN. Immutable and never deleted: the runtime role can read and insert only.
/// </summary>
internal sealed class SecurityAliasConfiguration : IEntityTypeConfiguration<SecurityAlias>
{
    public void Configure(EntityTypeBuilder<SecurityAlias> builder)
    {
        builder.ToTable("security_aliases", Schemas.Portfolio, t => t.HasCheckConstraint("ck_security_aliases_source", "source IN ('demat', 'dp', 'choice')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.NameKey).HasMaxLength(SecurityAlias.MaxNameKeyLength).IsRequired();
        builder.Property(x => x.Isin).HasMaxLength(Holding.MaxIsinLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(SecurityAlias.MaxNameLength).IsRequired();
        builder.Property(x => x.Source).HasConversion(EnumConversions.Lowercase<SecurityAliasSource>()).HasMaxLength(6).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(Transaction.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.AccountId, x.NameKey, x.Isin }).IsUnique().HasDatabaseName("ux_security_aliases_account_name_isin");
        builder.HasIndex(x => new { x.AccountId, x.Isin }).HasDatabaseName("ix_security_aliases_account_isin");
    }
}
