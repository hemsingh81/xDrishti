using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.DerivedBaskets;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>market.basket_versions</c> (BK-03, ADR 0258): insert-only. The unique index on <c>(basket_id, as_of, params_hash, coalesce(formed_at, '-infinity'))</c> is an expression index, written as raw SQL in the migration (EF Core cannot model it).
/// </summary>
internal sealed class BasketVersionConfiguration : IEntityTypeConfiguration<BasketVersionRecord>
{
    public void Configure(EntityTypeBuilder<BasketVersionRecord> builder)
    {
        builder.ToTable("basket_versions", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_basket_versions_outcome", "outcome IN ('applied', 'unchanged', 'not_formed')");
            t.HasCheckConstraint("ck_basket_versions_provenance", "provenance IN ('derived', 'recomputed')");
            t.HasCheckConstraint("ck_basket_versions_counts", "universe_size >= 0 AND member_count >= 0 AND below_threshold >= 0");
            t.HasCheckConstraint("ck_basket_versions_dates", "effective_from > as_of");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Params).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ParamsHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Excluded).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Provenance).HasMaxLength(10).IsRequired();
        builder.HasIndex(x => new { x.BasketId, x.AsOf }).HasDatabaseName("ix_basket_versions_basket_id_as_of");
        builder.HasOne<Basket>().WithMany().HasForeignKey(x => x.BasketId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BasketMemberScoreConfiguration : IEntityTypeConfiguration<BasketMemberScoreRecord>
{
    public void Configure(EntityTypeBuilder<BasketMemberScoreRecord> builder)
    {
        builder.ToTable("basket_member_scores", Schemas.Market, t => t.HasCheckConstraint("ck_basket_member_scores_rank", "rank >= 0 AND sessions_held >= 0"));
        builder.HasKey(x => new { x.VersionId, x.InstrumentId });
        builder.HasOne<BasketVersionRecord>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InstrumentRecord>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DerivedDefinitionConfiguration : IEntityTypeConfiguration<DerivedDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<DerivedDefinitionRecord> builder)
    {
        builder.ToTable("derived_definitions", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_derived_definitions_no", "definition_no BETWEEN 1 AND 14");
            t.HasCheckConstraint("ck_derived_definitions_cadence", "cadence IN ('daily', 'weekly', 'monthly', 'frozen_intraday')");
        });
        builder.HasKey(x => x.BasketId);
        builder.Property(x => x.BasketId).ValueGeneratedNever();
        builder.Property(x => x.Enabled).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.Cadence).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => x.DefinitionNo).IsUnique().HasDatabaseName("ux_derived_definitions_definition_no");
        builder.HasOne<Basket>().WithOne().HasForeignKey<DerivedDefinitionRecord>(x => x.BasketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BasketParamSetRecord>().WithMany().HasForeignKey(x => x.ParamsSetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BasketParamSetConfiguration : IEntityTypeConfiguration<BasketParamSetRecord>
{
    public void Configure(EntityTypeBuilder<BasketParamSetRecord> builder)
    {
        builder.ToTable("basket_param_sets", Schemas.Market, t => t.HasCheckConstraint("ck_basket_param_sets_no", "definition_no BETWEEN 1 AND 14"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Params).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ParamsHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
    }
}
