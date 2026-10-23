using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>research.model_designations</c> (spec P6-07, ADR 0070): champion / challenger / retired as an append-only record. A model's role is its latest row. Insert-only for the runtime role.</summary>
internal sealed class ModelDesignationRecordConfiguration : IEntityTypeConfiguration<ModelDesignationRecord>
{
    public void Configure(EntityTypeBuilder<ModelDesignationRecord> builder)
    {
        builder.ToTable("model_designations", Schemas.Research, t => t.HasCheckConstraint("ck_model_designations_role", "role IN ('challenger', 'champion', 'retired')"));
        builder.HasKey(x => x.Id).HasName("pk_model_designations");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Role).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<ModelVersionRecord>().WithMany().HasForeignKey(x => x.ModelId).HasConstraintName("fk_model_designations_model_versions_model_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ModelId, x.Id }).HasDatabaseName("ix_model_designations_model_id_id");
    }
}
