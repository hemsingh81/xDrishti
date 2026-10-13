namespace XDrishti.Domain.Platform.Services;

/// <summary>A part of the day, India time, both ends included.</summary>
public sealed record TimeRange(TimeOnly From, TimeOnly To)
{
    /// <summary>The ordinary session of the exchange: 09:15 to 15:30.</summary>
    public static readonly TimeRange MarketHours = new(new TimeOnly(9, 15), new TimeOnly(15, 30));

    public bool Contains(TimeOnly time) => time >= From && time <= To;

    public bool Intersects(TimeOnly from, TimeOnly to) => from <= To && to >= From;
}
