using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Platform;

/// <summary>
/// The one <c>Live:*</c> number the API reads (the others belong to the feed host, <c>XDrishti.Feed.LiveOptions</c>, and are documented there): how long "Run now" keeps the live feed up outside its window. Bound by every host that
/// uses <c>AddInfrastructure</c>, checked at start.
/// </summary>
public sealed class LiveManualRunOptions
{
    public const string SectionName = "Live";

    [Range(1, 240)]
    public int ManualRunMinutes { get; set; } = 30;
}
