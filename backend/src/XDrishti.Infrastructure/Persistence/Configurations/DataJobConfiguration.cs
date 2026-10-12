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
        {
            t.HasCheckConstraint("ck_data_jobs_status", "status IN ('running', 'succeeded', 'failed', 'skipped')");
            t.HasCheckConstraint("ck_data_jobs_trigger", "trigger IN ('manual', 'daily')");
        });
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
        builder.Property(x => x.Trigger)
            .HasConversion(EnumConversions.Lowercase<DataJobTrigger>())
            .HasMaxLength(6).HasDefaultValueSql("'manual'").ValueGeneratedNever().IsRequired();
        builder.HasIndex(x => new { x.Trigger, x.StartedAt }).HasDatabaseName("ix_data_jobs_trigger_started_at");
        builder.Property(x => x.RefetchGaps).HasDefaultValue(false).ValueGeneratedNever().IsRequired();
        builder.Property(x => x.ForceCsv).IsRequired();
        builder.Property(x => x.RebuildAll).IsRequired();
        builder.Property(x => x.CurrentPhase).HasMaxLength(9);
        builder.Property(x => x.Error).HasMaxLength(DataJob.MaxErrorLength);
        JsonColumn(builder.Property(x => x.Steps));
        JsonColumn(builder.Property(x => x.Warnings));
        JsonColumn(builder.Property(x => x.Csv));
        JsonColumn(builder.Property(x => x.Dhan));
        JsonColumn(builder.Property(x => x.Aggregate));
        JsonColumn(builder.Property(x => x.Quality)).HasDefaultValueSql(QualityNotRequested);

        // The one instrument a job is about (a fix action of the Data quality screen); null for a job over every instrument.
        builder.Property(x => x.InstrumentId);

        // The CSV files the job was asked to load (null: every file) and the file whose load a job undoes (ADR 0017 §6).
        JsonColumn(builder.Property(x => x.CsvFiles), required: false);
        builder.Property(x => x.UndoCsvFile).HasMaxLength(255);
    }

    /// <summary>The quality phase of the jobs that were recorded before it existed (and of any insert that leaves it out): not requested.</summary>
    internal const string QualityNotRequested =
        """'{"status":"notRequested","startedAt":null,"finishedAt":null,"done":0,"total":0,"message":null,"code":null,"runId":null,"passed":0,"warned":0,"failed":0}'::jsonb""";

    internal static PropertyBuilder<T> JsonColumn<T>(PropertyBuilder<T> property, bool required = true)
    {
        var converter = new ValueConverter<T, string>(v => JsonSerializer.Serialize(v, Json), v => JsonSerializer.Deserialize<T>(v, Json)!);
        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
            v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!);
        return property.HasConversion(converter, comparer).HasColumnType("jsonb").IsRequired(required);
    }
}
