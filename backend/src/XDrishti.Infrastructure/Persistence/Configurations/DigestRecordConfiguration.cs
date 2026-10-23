using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary><c>research.digests</c> (spec P7-15, ADR 0094): the weekly learning digest, one row per ISO week, insert-only for the runtime role. The database refuses a text that does not say nothing was applied.</summary>
internal sealed class DigestRecordConfiguration : IEntityTypeConfiguration<DigestRecord>
{
    public void Configure(EntityTypeBuilder<DigestRecord> builder)
    {
        builder.ToTable("digests", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_digests_shape", "iso_week ~ '^[0-9]{4}-W[0-9]{2}$' AND week_end = week_start + 6 AND position('Nothing was applied' IN text) > 0 AND length(built_by) > 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_digests");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.IsoWeek).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Text).IsRequired();
        builder.Property(x => x.SectionsJson).IsRequired();
        builder.Property(x => x.BuiltBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.IsoWeek).IsUnique().HasDatabaseName("uq_digests_iso_week"); // one digest per ISO week; the second insert is skipped
    }
}
