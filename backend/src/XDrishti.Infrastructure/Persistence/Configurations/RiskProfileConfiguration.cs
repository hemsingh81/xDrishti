using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class RiskProfileConfiguration : IEntityTypeConfiguration<RiskProfile>
{
    public void Configure(EntityTypeBuilder<RiskProfile> builder)
    {
        builder.ToTable("risk_profiles", Schemas.Accounts);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(RiskProfile.MaxNameLength).IsRequired(); // unique ignoring case: index created in the migration
        builder.Property(x => x.IsSeeded).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.RiskPerTradePct).HasPrecision(9, 4);
        builder.Property(x => x.MaxCapitalPerTradePct).HasPrecision(9, 4);
        builder.Property(x => x.MaxNewEntriesPerDay);
        builder.Property(x => x.MaxOpenPositions);
        builder.Property(x => x.MaxTotalRiskPct).HasPrecision(9, 4);
        builder.Property(x => x.MaxPerSector);
        builder.Property(x => x.MaxPairCorrelation).HasPrecision(5, 4);
        builder.Property(x => x.MaxIntraday);
        builder.Property(x => x.MaxSwing);
        builder.Property(x => x.DailyLossR).HasPrecision(9, 4);
        builder.Property(x => x.DailyLossPct).HasPrecision(9, 4);
        builder.Property(x => x.WeeklyLossR).HasPrecision(9, 4);
        builder.Property(x => x.WeeklyLossAction).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Ignore(x => x.Limits);
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}
