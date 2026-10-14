using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>portfolio.dividend_suggestions</c> (ADR 0026): the owner's decision about a ledger entry of class dividend — its <c>id</c> is the ledger entry's, so there is one decision per entry. Accepted (with the dividend created) or dismissed; never changed or deleted
/// (the runtime role has no DELETE).
/// </summary>
internal sealed class DividendSuggestionConfiguration : IEntityTypeConfiguration<DividendSuggestion>
{
    public void Configure(EntityTypeBuilder<DividendSuggestion> builder)
    {
        builder.ToTable("dividend_suggestions", Schemas.Portfolio, t =>
        {
            t.HasCheckConstraint("ck_dividend_suggestions_state", "state IN ('accepted', 'dismissed')");
            t.HasCheckConstraint("ck_dividend_suggestions_dividend", "(state = 'accepted') = (dividend_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<LedgerEntry>().WithOne().HasForeignKey<DividendSuggestion>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.AccountId).IsRequired();
        builder.HasOne<BrokerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.State).HasConversion(EnumConversions.Lowercase<DividendSuggestionState>()).HasMaxLength(9).IsRequired();
        builder.Property(x => x.DividendId);
        builder.HasOne<Dividend>().WithMany().HasForeignKey(x => x.DividendId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.DecidedBy).HasMaxLength(Dividend.MaxCreatedByLength).IsRequired();
        builder.Property(x => x.DecidedAt).IsRequired();

        builder.HasIndex(x => x.AccountId).HasDatabaseName("ix_dividend_suggestions_account");
    }
}
