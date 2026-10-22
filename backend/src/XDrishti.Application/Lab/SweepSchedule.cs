using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The sweep of a strategy: the parameters it takes (one may end with no point) and the points to run.</summary>
internal sealed record SweepSchedule(IReadOnlyList<string> Parameters, IReadOnlyList<SweepPoint> Points)
{
    /// <summary>No parameter and no point.</summary>
    public static SweepSchedule None { get; } = new([], []);
}
