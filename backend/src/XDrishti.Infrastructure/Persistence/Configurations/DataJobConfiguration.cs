using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Infrastructure.Persistence.Configurations;

internal sealed class DataJobConfiguration : IEntityTypeConfiguration<DataJob>
{
    /// <summary>The JSON of the phase and list columns: camelCase, like the API.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<DataJob> builder)
    {
        builder.ToTable("data_jobs", Schemas.Market, t =>
            t.HasCheckConstraint("ck_data_jobs_status", "status IN ('running', 'succeeded', 'failed')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RequestedBy).HasMaxLength(DataJob.MaxRequestedByLength).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.HasIndex(x => x.StartedAt);
        builder.Property(x => x.Status)
            .HasConversion(new ValueConverter<DataJobStatus, string>(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<DataJobStatus>(v, true)))
            .HasMaxLength(9).IsRequired();
        // At most one job runs at any time (the database backs the advisory lock).
        builder.HasIndex(x => x.Status, "ux_data_jobs_one_running").HasDatabaseName("ux_data_jobs_one_running").IsUnique().HasFilter("status = 'running'");
        builder.Property(x => x.ForceCsv).IsRequired();
        builder.Property(x => x.RebuildAll).IsRequired();
        builder.Property(x => x.CurrentPhase).HasMaxLength(9);
        builder.Property(x => x.Error).HasMaxLength(DataJob.MaxErrorLength);
        JsonColumn(builder.Property(x => x.Steps));
        JsonColumn(builder.Property(x => x.Warnings));
        JsonColumn(builder.Property(x => x.Csv));
        JsonColumn(builder.Property(x => x.Dhan));
        JsonColumn(builder.Property(x => x.Aggregate));
    }

    private static void JsonColumn<T>(PropertyBuilder<T> property)
    {
        var converter = new ValueConverter<T, string>(v => JsonSerializer.Serialize(v, Json), v => JsonSerializer.Deserialize<T>(v, Json)!);
        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
            v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!);
        property.HasConversion(converter, comparer).HasColumnType("jsonb").IsRequired();
    }
}
