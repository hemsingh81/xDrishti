using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class RefreshRunConfiguration : IEntityTypeConfiguration<RefreshRun>
{
    public void Configure(EntityTypeBuilder<RefreshRun> builder)
    {
        builder.ToTable("master_refreshes", Schemas.Market, t =>
        {
            t.HasCheckConstraint("ck_master_refreshes_trigger", "trigger IN ('manual', 'daily', 'forced')");
            t.HasCheckConstraint("ck_master_refreshes_status", "status IN ('running', 'succeeded', 'unchanged', 'failed')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Trigger).HasConversion(Lowercase<RefreshTrigger>()).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Force).IsRequired();
        builder.Property(x => x.RequestedBy).HasMaxLength(RefreshRun.MaxRequestedByLength).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.HasIndex(x => x.StartedAt);
        builder.Property(x => x.Status).HasConversion(Lowercase<RefreshStatus>()).HasMaxLength(10).IsRequired();
        // At most one refresh runs at any time (the database backs the advisory lock).
        builder.HasIndex(x => x.Status, "ux_master_refreshes_one_running").HasDatabaseName("ux_master_refreshes_one_running").IsUnique().HasFilter("status = 'running'");
        builder.Property(x => x.Phase).HasConversion(Lowercase<RefreshPhase>()).HasMaxLength(12);
        builder.Property(x => x.DhanETag).HasColumnName("dhan_etag").HasMaxLength(RefreshRun.MaxStampLength);
        builder.Property(x => x.DhanLastModified).HasMaxLength(RefreshRun.MaxStampLength);
        builder.Property(x => x.SectorETag).HasColumnName("sector_etag").HasMaxLength(RefreshRun.MaxStampLength);
        builder.Property(x => x.SectorLastModified).HasMaxLength(RefreshRun.MaxStampLength);
        builder.Property(x => x.RulesVersion).IsRequired();
        builder.Property(x => x.SectorsApplied).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(RefreshRun.MaxErrorLength);

        var warnings = new ValueConverter<IReadOnlyList<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
        builder.Property(x => x.Warnings).HasConversion(warnings, new ValueComparer<IReadOnlyList<string>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            v => v.ToList())).HasColumnType("jsonb").IsRequired();

        var kept = new ValueConverter<IReadOnlyDictionary<string, int>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, int>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, int>());
        builder.Property(x => x.KeptByType).HasConversion(kept, new ValueComparer<IReadOnlyDictionary<string, int>>(
            (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
            v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.Key, item.Value)),
            v => new Dictionary<string, int>(v))).HasColumnType("jsonb").IsRequired();
    }

    private static ValueConverter<T, string> Lowercase<T>()
        where T : struct, Enum =>
        new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<T>(v, true));
}
