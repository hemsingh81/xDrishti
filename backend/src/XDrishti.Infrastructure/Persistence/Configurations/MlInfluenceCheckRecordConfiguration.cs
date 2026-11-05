using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>research.ml_influence_checks</c> (spec CF-10 part 10c-2, ADR 0217): how the influence predicate stood for a model, with the hash of the settings. Insert-only for the runtime role.</summary>
internal sealed class MlInfluenceCheckRecordConfiguration : IEntityTypeConfiguration<MlInfluenceCheckRecord>
{
    public void Configure(EntityTypeBuilder<MlInfluenceCheckRecord> builder)
    {
        builder.ToTable("ml_influence_checks", Schemas.Research, t => t.HasCheckConstraint("ck_ml_influence_checks_hashes", "setting_hash ~ '^[0-9a-f]{64}$' AND inputs_hash ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(x => x.Id).HasName("pk_ml_influence_checks");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.EvaluatedAt).IsRequired();
        builder.Property(x => x.MayInfluence).IsRequired();
        builder.Property(x => x.Terms).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SettingHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.InputsHash).HasColumnType("character(64)").IsRequired();
        builder.HasOne<ModelVersionRecord>().WithMany().HasForeignKey(x => x.ModelId).HasConstraintName("fk_ml_influence_checks_model_versions_model_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ModelId, x.Id }).HasDatabaseName("ix_ml_influence_checks_model_id_id");
    }
}
