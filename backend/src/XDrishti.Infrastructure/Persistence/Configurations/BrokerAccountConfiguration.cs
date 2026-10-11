using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class BrokerAccountConfiguration : IEntityTypeConfiguration<BrokerAccount>
{
    public void Configure(EntityTypeBuilder<BrokerAccount> builder)
    {
        builder.ToTable("broker_accounts", Schemas.Accounts, t =>
            // A primary or standby Data account has the Data role on, and nobody is both. (The "only one of each" rule is the partial unique indexes below.)
            t.HasCheckConstraint("ck_broker_accounts_data_role", "NOT (data_primary AND data_standby) AND (data_enabled OR NOT (data_primary OR data_standby))"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Slug).HasMaxLength(BrokerAccount.MaxSlugLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(BrokerAccount.MaxNameLength).IsRequired();
        builder.Property(x => x.Broker).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ClientId).HasMaxLength(BrokerAccount.MaxClientIdLength).IsRequired();
        builder.Property(x => x.ApiKeyIssuedOn).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(7).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(BrokerAccount.MaxNotesLength);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.ArchivedAt);
        builder.Property(x => x.DataEnabled).IsRequired();
        builder.Property(x => x.DataPrimary).IsRequired();
        builder.Property(x => x.DataStandby).IsRequired();
        builder.Property(x => x.LiveFeedBudget).IsRequired();

        // Names are unique among accounts that are not archived (an archived account frees its name).
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("archived_at IS NULL").HasDatabaseName("ux_broker_accounts_slug_active");

        // Exactly one primary and at most one standby Data account: a unique index over a boolean that is only indexed while true.
        builder.HasIndex(x => x.DataPrimary).IsUnique().HasFilter("data_primary AND archived_at IS NULL").HasDatabaseName("ux_broker_accounts_data_primary");
        builder.HasIndex(x => x.DataStandby).IsUnique().HasFilter("data_standby AND archived_at IS NULL").HasDatabaseName("ux_broker_accounts_data_standby");

        builder.OwnsOne(x => x.Trading, trading =>
        {
            trading.ToTable("trading_settings", Schemas.Accounts);
            trading.WithOwner().HasForeignKey("AccountId");
            trading.HasKey("AccountId");
            trading.Property(x => x.Enabled).IsRequired();
            trading.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16).IsRequired();
            trading.Property(x => x.RiskProfileId);
            trading.HasIndex(x => x.RiskProfileId);
            trading.HasOne<RiskProfile>().WithMany().HasForeignKey(x => x.RiskProfileId).OnDelete(DeleteBehavior.SetNull);
            trading.Property(x => x.CapitalSource).HasConversion<string>().HasMaxLength(16).IsRequired();
            trading.Property(x => x.CapitalAmount).HasPrecision(18, 2);
            trading.Property(x => x.Intraday).IsRequired();
            trading.Property(x => x.Swing).IsRequired();
            trading.Property(x => x.IntradayInstruments).IsRequired();
            trading.Property(x => x.SwingBuyInstruments).IsRequired();
            trading.Property(x => x.SwingSellInstruments).IsRequired();
            trading.Property(x => x.OverrideRiskPerTradePct).HasPrecision(9, 4);
            trading.Property(x => x.OverrideDailyLossPct).HasPrecision(9, 4);
            trading.Ignore(x => x.Overrides);
        });
        builder.Navigation(x => x.Trading).IsRequired();

        builder.OwnsOne(x => x.Portfolio, portfolio =>
        {
            portfolio.ToTable("portfolio_settings", Schemas.Accounts);
            portfolio.WithOwner().HasForeignKey("AccountId");
            portfolio.HasKey("AccountId");
            portfolio.Property(x => x.Enabled).IsRequired();
            portfolio.Property(x => x.DefaultBucket).HasMaxLength(PortfolioBucket.MaxNameLength);
            portfolio.HasOne<PortfolioBucket>().WithMany().HasPrincipalKey(x => x.Name).HasForeignKey(x => x.DefaultBucket).OnDelete(DeleteBehavior.Restrict);
            portfolio.Property(x => x.HistoryFrom);
            portfolio.Property(x => x.IncludeInTotals).IsRequired();
        });
        builder.Navigation(x => x.Portfolio).IsRequired();

        // Optimistic concurrency (see OwnerAccountConfiguration): two requests changing the same account cannot overwrite each other.
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}
