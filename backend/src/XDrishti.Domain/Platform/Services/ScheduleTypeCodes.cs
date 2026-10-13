namespace XDrishti.Domain.Platform.Services;

/// <summary>The wire and storage codes of <see cref="ScheduleType"/>: daily · weekly · after · window · interval · continuous.</summary>
public static class ScheduleTypeCodes
{
    public static string ToCode(this ScheduleType type) => type.ToString().ToLowerInvariant();

    public static bool TryParse(string? code, out ScheduleType type)
    {
        foreach (var candidate in Enum.GetValues<ScheduleType>())
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.Ordinal))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}
