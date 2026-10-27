using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>ai.eval_marker</c> (spec P9-09 AC 13): at most one row (the key is 1). Written by the fixture seeder as the database owner, last; the runtime role has no right on the table.</summary>
internal sealed class AiEvalMarkerRecordConfiguration : IEntityTypeConfiguration<AiEvalMarkerRecord>
{
    public void Configure(EntityTypeBuilder<AiEvalMarkerRecord> builder)
    {
        builder.ToTable("eval_marker", Schemas.Ai, t => t.HasCheckConstraint("ck_ai_eval_marker_single", "id = 1"));
        builder.HasKey(x => x.Id).HasName("pk_eval_marker");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FixtureHash).HasColumnType("character(64)").IsRequired();
        builder.Property(x => x.SeededAt).HasDefaultValueSql("now()");
    }
}
