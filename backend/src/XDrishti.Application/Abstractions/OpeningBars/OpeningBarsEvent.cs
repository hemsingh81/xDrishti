namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>What the recorder tells the derived job about a day: it <see cref="Started"/> (subscribed), had a <see cref="Gap"/> (a drop or a silence; <c>To</c> null while open) or stopped for <see cref="LowDisk"/>.</summary>
public sealed record OpeningBarsEvent(string Kind, DateTimeOffset From, DateTimeOffset? To = null)
{
    public const string Started = "started";

    public const string Gap = "gap";

    public const string LowDisk = "low_disk";
}
