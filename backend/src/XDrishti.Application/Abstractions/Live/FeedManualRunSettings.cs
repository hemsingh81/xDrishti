namespace XDrishti.Application.Abstractions.Live;

/// <summary>How long a "Run now" keeps the live feed up outside its window (<c>Live:ManualRunMinutes</c>, 1 to 240, default 30). Read by every host that can serve Run now.</summary>
public sealed record FeedManualRunSettings(TimeSpan Duration)
{
    public static readonly FeedManualRunSettings Default = new(TimeSpan.FromMinutes(30));
}
