using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// The stored choice for one service (ADR 0018 §1, table <c>service_schedules</c>): one row per service, created by the first save or by the first-start seed; a service without a row runs on its
/// definition's defaults. <see cref="Revision"/> is the row version (the database's <c>xmin</c>): a save made on an older one is refused. "Restore default" writes the defaults; nothing is deleted.
/// </summary>
public sealed class ServiceSchedule : Entity<string>
{
    public const int MaxUpdatedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private ServiceSchedule(string id, bool enabled, Schedule schedule, int retries, int timeoutMinutes, bool catchUp, bool notifyOnFailure, IReadOnlyDictionary<string, JsonElement> parameters, string updatedBy, DateTimeOffset updatedAt)
        : base(id)
    {
        Enabled = enabled;
        Schedule = schedule;
        Retries = retries;
        TimeoutMinutes = timeoutMinutes;
        CatchUp = catchUp;
        NotifyOnFailure = notifyOnFailure;
        Parameters = parameters;
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
    }

    public bool Enabled { get; private set; }

    public Schedule Schedule { get; private set; }

    public int Retries { get; private set; }

    public int TimeoutMinutes { get; private set; }

    public bool CatchUp { get; private set; }

    public bool NotifyOnFailure { get; private set; }

    public IReadOnlyDictionary<string, JsonElement> Parameters { get; private set; }

    public string UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The row version, set by the database; 0 before the row is first saved.</summary>
    public uint Revision { get; private set; }

    /// <summary>False when the stored schedule could not be read; the other choices of the row are still good.</summary>
    public bool ScheduleReadable => Schedule != Schedule.Unreadable;

    public ServiceSettings Settings => new(Enabled, Schedule, Retries, TimeoutMinutes, CatchUp, NotifyOnFailure, Parameters);

    /// <summary>A row for a settings that were checked by <see cref="ScheduleRules"/>.</summary>
    public static ServiceSchedule Create(string serviceId, ServiceSettings settings, string updatedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(serviceId, settings.Enabled, settings.Schedule, settings.Retries, settings.TimeoutMinutes, settings.CatchUp, settings.NotifyOnFailure, settings.Parameters, Who(updatedBy), now);
    }

    /// <summary>Replaces every choice with the (checked) settings.</summary>
    public void Apply(ServiceSettings settings, string updatedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Enabled = settings.Enabled;
        Schedule = settings.Schedule;
        Retries = settings.Retries;
        TimeoutMinutes = settings.TimeoutMinutes;
        CatchUp = settings.CatchUp;
        NotifyOnFailure = settings.NotifyOnFailure;
        Parameters = settings.Parameters;
        UpdatedBy = Who(updatedBy);
        UpdatedAt = now;
    }

    private static string Who(string value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "unknown" : trimmed.Length > MaxUpdatedByLength ? trimmed[..MaxUpdatedByLength] : trimmed;
    }
}
