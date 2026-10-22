using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>lab.strategy_versions</c> (spec P3-02d, D2): one row per version, written once and never changed. The hash is unique (the same content is one row; a library lookup by hash is the idempotence of an append) and the parent hash points at it.
/// The canonical text is <c>text</c>, not <c>jsonb</c>, so the bytes that were hashed are the bytes kept (ADR 0045 D1). Insert-only for the runtime role.
/// </summary>
internal sealed class StrategyVersionRowConfiguration : IEntityTypeConfiguration<StrategyVersionRow>
{
    public void Configure(EntityTypeBuilder<StrategyVersionRow> builder)
    {
        builder.ToTable("strategy_versions", Schemas.Lab, t =>
        {
            t.HasCheckConstraint("ck_strategy_versions_version", "version >= 1");
            t.HasCheckConstraint("ck_strategy_versions_hash", "hash ~ '^[0-9a-f]{64}$' AND (parent_hash IS NULL OR parent_hash ~ '^[0-9a-f]{64}$')");
            t.HasCheckConstraint("ck_strategy_versions_sizes", "octet_length(source_text) <= 262144 AND (note IS NULL OR char_length(note) <= 500)");
        });
        builder.HasKey(x => new { x.StrategyId, x.Version }).HasName("pk_strategy_versions");
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.SchemaVersion).IsRequired();
        builder.Property(x => x.Hash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.SourceText).HasColumnType("text").IsRequired();
        builder.Property(x => x.CanonicalJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.ParentHash).HasColumnType("character(64)");
        builder.Property(x => x.Note).HasColumnType("text");
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasAlternateKey(x => x.Hash).HasName("uq_strategy_versions_hash");
        builder.HasOne<StrategyRow>().WithMany().HasForeignKey(x => x.StrategyId).HasConstraintName("fk_strategy_versions_strategy").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StrategyVersionRow>().WithMany().HasForeignKey(x => x.ParentHash).HasPrincipalKey(x => x.Hash).HasConstraintName("fk_strategy_versions_parent").OnDelete(DeleteBehavior.Restrict);
    }
}
