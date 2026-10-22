using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>lab.strategies</c> (spec P3-02d, D1): one row per strategy family, written once. The id follows the model's rule; a unique index on <c>lower(id)</c> (added in the migration, EF cannot express it) keeps two ids that look alike out. There is no
/// latest-version column: it would have to be updated, and the runtime role cannot. Insert-only for the runtime role.
/// </summary>
internal sealed class StrategyRowConfiguration : IEntityTypeConfiguration<StrategyRow>
{
    public void Configure(EntityTypeBuilder<StrategyRow> builder)
    {
        builder.ToTable("strategies", Schemas.Lab, t =>
        {
            t.HasCheckConstraint("ck_strategies_id", "id ~ '^[A-Za-z][A-Za-z0-9_]{0,63}$'");
            t.HasCheckConstraint("ck_strategies_style", "style IN ('intraday', 'swing')");
        });
        builder.HasKey(x => x.Id).HasName("pk_strategies");
        builder.Property(x => x.Id).HasColumnType("text").IsRequired();
        builder.Property(x => x.Style).HasColumnType("text").IsRequired();
        builder.Property(x => x.BuiltIn).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
