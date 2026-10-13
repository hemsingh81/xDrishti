using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>platform.service_runs</c> (ADR 0018 §5): the history of runs and attempts of the services — trigger (scheduled · catch_up · after · manual), attempt, start, end, status, message and the data job a run
/// started. At most one run per service is <c>running</c> (a unique partial index). Rows are history and never deleted.
/// </summary>
internal sealed class ServiceRunConfiguration : IEntityTypeConfiguration<ServiceRun>
{
    public void Configure(EntityTypeBuilder<ServiceRun> builder)
    {
        builder.ToTable("service_runs", Schemas.Platform, t =>
        {
            t.HasCheckConstraint("ck_service_runs_trigger", "trigger IN ('scheduled', 'catch_up', 'after', 'manual')");
            t.HasCheckConstraint("ck_service_runs_status", "status IN ('running', 'succeeded', 'failed', 'skipped')");
            t.HasCheckConstraint("ck_service_runs_attempt", "attempt >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ServiceId).HasMaxLength(ServiceDefinition.MaxIdLength).IsRequired();
        builder.Property(x => x.Trigger)
            .HasConversion(new ValueConverter<ServiceRunTrigger, string>(v => v.ToCode(), v => ServiceRunCodes.ParseTrigger(v)))
            .HasMaxLength(9).IsRequired();
        builder.Property(x => x.Attempt).HasConversion<short>().HasColumnType("smallint").IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.FinishedAt);
        builder.Property(x => x.Status).HasConversion(EnumConversions.Lowercase<ServiceRunStatus>()).HasMaxLength(9).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(ServiceRun.MaxMessageLength);
        builder.Property(x => x.DataJobId);

        builder.HasIndex(x => new { x.ServiceId, x.StartedAt }).IsDescending(false, true).HasDatabaseName("ix_service_runs_service_id_started_at");

        // A service runs at most once at a time (the database backs the scheduler's own check, also across instances).
        builder.HasIndex(x => x.ServiceId).IsUnique().HasFilter("status = 'running'").HasDatabaseName("ux_service_runs_one_running");
    }
}
