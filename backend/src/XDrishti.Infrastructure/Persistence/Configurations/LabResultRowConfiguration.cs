using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>lab.lab_results</c> (spec P5-04, ADR 0060): the verdict of a full test, one row per Lab run (unique), insert-only for the runtime role. The verdict, its gates, the cells and the exploration block are one document in <c>result</c>.
/// </summary>
internal sealed class LabResultRowConfiguration : IEntityTypeConfiguration<LabResultRow>
{
    public void Configure(EntityTypeBuilder<LabResultRow> builder)
    {
        builder.ToTable("lab_results", Schemas.Lab, t => t.HasCheckConstraint("ck_lab_results_outcome", "outcome IN ('PASS', 'MARGINAL', 'FAIL')"));
        builder.HasKey(x => x.Id).HasName("pk_lab_results");
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.StrategyId).HasColumnType("text").IsRequired();
        builder.Property(x => x.StrategyHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.Method).HasColumnType("text").IsRequired();
        builder.Property(x => x.Outcome).HasColumnType("text").IsRequired();
        builder.Property(x => x.VariantCount).IsRequired();
        builder.Property(x => x.AdjustmentFactor).IsRequired();
        builder.Property(x => x.Result).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.LabRunId).IsUnique().HasDatabaseName("uq_lab_results_run");
        builder.HasOne<LabRunRow>().WithMany().HasForeignKey(x => x.LabRunId).HasConstraintName("fk_lab_results_run").OnDelete(DeleteBehavior.Restrict);
    }
}
