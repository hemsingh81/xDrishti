using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>platform.service_schedules</c> (ADR 0018 §1): one row per service with the owner's choices — enabled, the schedule (JSON of <see cref="ScheduleSpec"/>), retries, timeout, catch-up, notify on failure,
/// parameters (JSON) — and who changed it when. Optimistic concurrency by the row version (<c>xmin</c>): a save made on a row someone changed meanwhile is refused. Rows are never deleted ("restore default" writes
/// the defaults).
/// </summary>
internal sealed class ServiceScheduleConfiguration : IEntityTypeConfiguration<ServiceSchedule>
{
    public void Configure(EntityTypeBuilder<ServiceSchedule> builder)
    {
        builder.ToTable("service_schedules", Schemas.Platform, t =>
        {
            t.HasCheckConstraint("ck_service_schedules_retries", "retries BETWEEN 0 AND 5");
            t.HasCheckConstraint("ck_service_schedules_timeout", "timeout_minutes BETWEEN 1 AND 240");
            t.HasCheckConstraint("ck_service_schedules_schedule", "schedule ->> 'type' IN ('daily', 'weekly', 'after', 'window', 'interval', 'continuous')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("service_id").HasMaxLength(ServiceDefinition.MaxIdLength).ValueGeneratedNever();
        builder.Property(x => x.Enabled).IsRequired();
        builder.Property(x => x.Schedule)
            .HasConversion(
                new ValueConverter<Schedule, string>(v => JsonSerializer.Serialize(ScheduleSpec.Of(v), DataJobConfiguration.Json), v => Read(v)),
                new ValueComparer<Schedule>(
                    (a, b) => a == b,
                    v => v.GetHashCode(),
                    v => v))
            .HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Retries).HasConversion<short>().HasColumnType("smallint").IsRequired();
        builder.Property(x => x.TimeoutMinutes).HasConversion<short>().HasColumnType("smallint").IsRequired();
        builder.Property(x => x.CatchUp).IsRequired();
        builder.Property(x => x.NotifyOnFailure).IsRequired();
        builder.Property(x => x.Parameters)
            .HasConversion(
                new ValueConverter<IReadOnlyDictionary<string, JsonElement>, string>(v => JsonSerializer.Serialize(v, DataJobConfiguration.Json), v => ReadParameters(v)),
                new ValueComparer<IReadOnlyDictionary<string, JsonElement>>(
                    (a, b) => JsonSerializer.Serialize(a, DataJobConfiguration.Json) == JsonSerializer.Serialize(b, DataJobConfiguration.Json),
                    v => JsonSerializer.Serialize(v, DataJobConfiguration.Json).GetHashCode(StringComparison.Ordinal),
                    v => ReadParameters(JsonSerializer.Serialize(v, DataJobConfiguration.Json))))
            .HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(ServiceSchedule.MaxUpdatedByLength).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        // PostgreSQL's system column xmin changes with every update of the row: a save based on a row someone changed meanwhile matches nothing and is refused.
        builder.Property(x => x.Revision).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        builder.Ignore(x => x.Settings);
    }

    /// <summary>The schedule of a stored row; a text that cannot be read (damaged JSON, a time that does not parse) is <see cref="Schedule.Unreadable"/>: that one service degrades, the rest keep working.</summary>
    private static Schedule Read(string json)
    {
        try
        {
            var spec = JsonSerializer.Deserialize<ScheduleSpec>(json, DataJobConfiguration.Json);
            return spec?.Parse(string.Empty, new List<ScheduleIssue>()) ?? Schedule.Unreadable;
        }
        catch (JsonException)
        {
            return Schedule.Unreadable;
        }
    }

    /// <summary>The parameters of a stored row; a text that cannot be read is none (the schema's defaults apply).</summary>
    private static Dictionary<string, JsonElement> ReadParameters(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, DataJobConfiguration.Json) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }
}
